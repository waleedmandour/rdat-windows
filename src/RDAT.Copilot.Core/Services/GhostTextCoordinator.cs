using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RDAT.Copilot.Core.Constants;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Core.Services;

/// <summary>
/// Ghost Text Coordinator — the central orchestrator for all ghost text channels.
/// Bridges the editor event loop (cursor/text changes) with the LLM queue and
/// RAG pipeline to deliver contextually relevant inline completions.
///
/// Channel flow:
///   Editor Event → Coordinator → Debounce Timer → Channel Handler → LLM Queue → Result → Monaco
///
/// Channel Handlers:
///   - OnSourceChanged → Prefetch (Ch3): Generate dual versions for source N+3
///   - OnTargetCursorChanged → Burst (Ch5) + Pause (Ch6): Debounced ghost text
///   - OnTargetCursorChanged → GTR (Phase 2): RAG TM match lookup
///
/// Debounce rules:
///   - Burst: 800ms after last keystroke (AppConstants.BurstDebounceMs)
///   - Pause: 1200ms after last keystroke (AppConstants.PauseDebounceMs)
///   - Prefetch: Immediate on source sentence change (no debounce)
/// </summary>
public sealed class GhostTextCoordinator : IGhostTextCoordinator, IDisposable
{
    private readonly ILocalInferenceService _inferenceService;
    private readonly ILlmQueueService _queueService;
    private readonly IRagPipelineService? _ragPipeline;
    private readonly ILogger<GhostTextCoordinator> _logger;

    // Debounce timers
    private CancellationTokenSource? _burstCts;
    private CancellationTokenSource? _pauseCts;

    // Current editor state
    private string _currentTargetText = string.Empty;
    private string _currentSourceText = string.Empty;
    private int _currentTargetLine = 1;
    private int _currentTargetColumn = 1;
    private string _lastProcessedSourceHash = string.Empty;

    // System prompts per channel
    private const string PrefetchSystemPrompt =
        """You are a professional English-Arabic translator. Translate the following sentence naturally. Provide ONLY the translation, nothing else. Output two versions: one formal and one natural.""";

    private const string BurstSystemPrompt =
        """You are a professional English-Arabic translator. Complete the partial Arabic translation. Output ONLY the continuation (3-5 words), nothing else. Do not repeat what's already written.""";

    private const string PauseSystemPrompt =
        """You are a professional English-Arabic translator. Continue the Arabic translation based on context. Output ONLY the continuation (5-20 words), nothing else. Maintain consistent register and terminology.""";

    public bool IsActive { get; private set; }

    public event EventHandler<GhostTextSuggestion>? SuggestionReady;
    public event EventHandler<string>? ClearSuggestion;

    public GhostTextCoordinator(
        ILocalInferenceService inferenceService,
        ILlmQueueService queueService,
        IRagPipelineService? ragPipeline,
        ILogger<GhostTextCoordinator> logger)
    {
        _inferenceService = inferenceService;
        _queueService = queueService;
        _ragPipeline = ragPipeline;
        _logger = logger;

        // Subscribe to queue generation events
        _queueService.GenerationCompleted += OnGenerationCompleted;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        IsActive = true;
        _logger.LogInformation("[GhostText] Coordinator started");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync()
    {
        IsActive = false;

        // Cancel all pending debounces
        CancelBurstTimer();
        CancelPauseTimer();

        // Clear queue pending
        _queueService.ClearPending();

        // Stop the queue
        await _queueService.StopAsync().ConfigureAwait(false);

        _logger.LogInformation("[GhostText] Coordinator stopped");
    }

    /// <inheritdoc/>
    public Task OnTargetCursorChangedAsync(int lineNumber, int column, CancellationToken cancellationToken = default)
    {
        if (!IsActive) return Task.CompletedTask;

        _currentTargetLine = lineNumber;
        _currentTargetColumn = column;

        if (!_inferenceService.IsReady) return Task.CompletedTask;

        // Get current and previous line text from target
        var currentLine = GetTargetLine(lineNumber);
        var previousLine = GetTargetLine(lineNumber - 1);
        var textBeforeCursor = column > 1 ? currentLine[..(column - 1)] : string.Empty;

        // Only trigger if there's something typed on the current line
        if (string.IsNullOrWhiteSpace(textBeforeCursor)) return Task.CompletedTask;

        // ─── Channel 5: Burst (0.8s debounce) ─────────────────────────
        CancelBurstTimer();
        _burstCts = new CancellationTokenSource();
        var burstToken = _burstCts.Token;
        var burstLine = lineNumber;
        var burstCol = column;
        var burstContext = textBeforeCursor;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(AppConstants.BurstDebounceMs, burstToken).ConfigureAwait(false);
                if (burstToken.IsCancellationRequested) return;

                await FireBurstChannelAsync(burstLine, burstCol, burstContext, previousLine, burstToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
        }, burstToken);

        // ─── Channel 6: Pause (1.2s debounce) ─────────────────────────
        CancelPauseTimer();
        _pauseCts = new CancellationTokenSource();
        var pauseToken = _pauseCts.Token;
        var pauseLine = lineNumber;
        var pauseCol = column;
        var pauseContext = textBeforeCursor;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(AppConstants.PauseDebounceMs, pauseToken).ConfigureAwait(false);
                if (pauseToken.IsCancellationRequested) return;

                await FirePauseChannelAsync(pauseLine, pauseCol, pauseContext, previousLine, pauseToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
        }, pauseToken);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task OnTargetTextChangedAsync(string fullText, CancellationToken cancellationToken = default)
    {
        if (!IsActive) return Task.CompletedTask;

        _currentTargetText = fullText;

        // Clear stale suggestions when text changes significantly
        ClearSuggestion?.Invoke(this, "all");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task OnSourceTextChangedAsync(string fullText, CancellationToken cancellationToken = default)
    {
        if (!IsActive) return;
        if (!_inferenceService.IsReady) return;

        _currentSourceText = fullText;

        // Parse sentences and find the next one to prefetch (N+3)
        var sentences = fullText
            .Split(new[] { '\n', '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 10)
            .ToList();

        if (sentences.Count == 0) return;

        // Hash the sentences to detect changes
        var hash = string.Join("|", sentences);
        if (hash == _lastProcessedSourceHash) return;
        _lastProcessedSourceHash = hash;

        // Prefetch the next 1-2 sentences beyond what's being actively translated
        var prefetchStart = Math.Min(_currentTargetLine + 2, sentences.Count);
        var prefetchCount = Math.Min(2, sentences.Count - prefetchStart);

        for (int i = 0; i < prefetchCount; i++)
        {
            var sourceSentence = sentences[prefetchStart + i];

            if (!string.IsNullOrWhiteSpace(sourceSentence))
            {
                await FirePrefetchChannelAsync(sourceSentence).ConfigureAwait(false);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Channel Handlers
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Channel 5 (Burst): 3–5 word autocomplete after 0.8s pause.
    /// Priority: Medium-High — preempts Prefetch.
    /// </summary>
    private async Task FireBurstChannelAsync(
        int line, int col, string textBeforeCursor,
        string previousLine, CancellationToken cancellationToken)
    {
        var prompt = BuildBurstPrompt(textBeforeCursor, previousLine);

        if (string.IsNullOrWhiteSpace(prompt)) return;

        _logger.LogDebug("[GhostText] Ch5 (Burst): Firing for L{Line}:C{Col}", line, col);

        var request = new LlmRequest
        {
            Priority = LlmRequestPriority.Burst,
            Channel = "burst",
            SystemPrompt = BurstSystemPrompt,
            UserMessage = prompt,
            MaxTokens = AppConstants.BurstMaxTokens,
            Temperature = AppConstants.DefaultTemperature,
            TargetLine = line,
            TargetColumn = col
        };

        await _queueService.EnqueueAsync(request).ConfigureAwait(false);
    }

    /// <summary>
    /// Channel 6 (Pause): 5–20 word continuation after 1.2s pause.
    /// Priority: High — preempts Prefetch and Burst.
    /// </summary>
    private async Task FirePauseChannelAsync(
        int line, int col, string textBeforeCursor,
        string previousLine, CancellationToken cancellationToken)
    {
        var prompt = await BuildPausePromptAsync(textBeforeCursor, previousLine, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(prompt)) return;

        _logger.LogDebug("[GhostText] Ch6 (Pause): Firing for L{Line}:C{Col}", line, col);

        var request = new LlmRequest
        {
            Priority = LlmRequestPriority.Pause,
            Channel = "pause",
            SystemPrompt = PauseSystemPrompt,
            UserMessage = prompt,
            MaxTokens = AppConstants.PauseMaxTokens,
            Temperature = AppConstants.DefaultTemperature,
            TargetLine = line,
            TargetColumn = col
        };

        await _queueService.EnqueueAsync(request).ConfigureAwait(false);
    }

    /// <summary>
    /// Channel 3 (Prefetch): Background dual-version translation for source N+3.
    /// Priority: Low — preempted by Burst and Pause.
    /// Results are cached for later use.
    /// </summary>
    private async Task FirePrefetchChannelAsync(string sourceSentence)
    {
        if (string.IsNullOrWhiteSpace(sourceSentence)) return;

        _logger.LogDebug("[GhostText] Ch3 (Prefetch): Queueing for \"{Sentence}\"",
            sourceSentence.Length > 50 ? sourceSentence[..50] + "..." : sourceSentence);

        var request = new LlmRequest
        {
            Priority = LlmRequestPriority.Prefetch,
            Channel = "prefetch",
            SystemPrompt = PrefetchSystemPrompt,
            UserMessage = $"Translate: {sourceSentence}",
            MaxTokens = AppConstants.PrefetchMaxTokens,
            Temperature = AppConstants.DefaultTemperature + 0.1f, // Slightly more creative
            SourceSentence = sourceSentence,
            TargetLine = 0,
            TargetColumn = 0
        };

        await _queueService.EnqueueAsync(request).ConfigureAwait(false);
    }

    // ════════════════════════════════════════════════════════════════
    // Prompt Builders
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Build a concise prompt for burst autocomplete (3-5 words).
    /// Includes the current partial text and the source sentence for context.
    /// </summary>
    private string BuildBurstPrompt(string textBeforeCursor, string previousLine)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("Complete this translation (3-5 words only):\n\n");

        if (!string.IsNullOrWhiteSpace(previousLine))
        {
            sb.Append($"[Previous line]: {previousLine}\n");
        }

        var sourceSentence = GetSourceSentenceForLine(_currentTargetLine);
        if (!string.IsNullOrWhiteSpace(sourceSentence))
        {
            sb.Append($"[Source]: {sourceSentence}\n");
        }

        sb.Append($"[Partial translation]: ...{textBeforeCursor[^Math.Min(100, textBeforeCursor.Length)..]}");

        return sb.ToString();
    }

    /// <summary>
    /// Build a context-rich prompt for pause continuation (5-20 words).
    /// Includes previous lines, source sentence, and partial translation.
    /// </summary>
    private async Task<string> BuildPausePromptAsync(string textBeforeCursor, string previousLine, CancellationToken cancellationToken)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("Continue this translation (5-20 words):\n\n");

        // Include 2 previous lines for register continuity
        var prevLine2 = GetTargetLine(_currentTargetLine - 2);
        if (!string.IsNullOrWhiteSpace(prevLine2))
        {
            sb.Append($"[Two lines above]: {prevLine2}\n");
        }

        if (!string.IsNullOrWhiteSpace(previousLine))
        {
            sb.Append($"[Previous line]: {previousLine}\n");
        }

        var sourceSentence = GetSourceSentenceForLine(_currentTargetLine);
        if (!string.IsNullOrWhiteSpace(sourceSentence))
        {
            sb.Append($"[Source]: {sourceSentence}\n");
        }

        // RAG context augmentation
        if (_ragPipeline?.IsReady == true && !string.IsNullOrWhiteSpace(sourceSentence))
        {
            try
            {
                var ragMatch = await _ragPipeline.GetBestMatchAsync(sourceSentence, cancellationToken).ConfigureAwait(false);
                if (ragMatch is not null && ragMatch.Score >= 0.8)
                {
                    sb.Append($"[Reference translation]: {ragMatch.Entry.TargetText}\n");
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[GhostText] RAG lookup failed for pause prompt");
            }
        }

        sb.Append($"[Partial translation]: ...{textBeforeCursor[^Math.Min(150, textBeforeCursor.Length)..]}");

        return sb.ToString();
    }

    // ════════════════════════════════════════════════════════════════
    // Event Handlers
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Handle generation results from the queue and route to Monaco.
    /// </summary>
    private void OnGenerationCompleted(object? sender, LlmGenerationResult result)
    {
        if (!result.Success || string.IsNullOrEmpty(result.GeneratedText))
        {
            if (result.WasPreempted)
            {
                _logger.LogDebug("[GhostText] {Channel} preempted", result.Channel);
            }

            return;
        }

        // Route the result to the appropriate ghost text channel
        var suggestion = new GhostTextSuggestion(
            Channel: result.Channel,
            InsertText: result.GeneratedText,
            StartLine: _currentTargetLine,
            StartColumn: _currentTargetColumn,
            ProviderId: $"rdat-{result.Channel}",
            Label: result.Channel switch
            {
                "burst" => "[Tab] Autocomplete",
                "pause" => "[Tab] Complete",
                "prefetch" => "[Tab] Prefetched",
                _ => "[Tab] Suggestion"
            }
        );

        _logger.LogInformation(
            "[GhostText] {Channel} suggestion ready: \"{Text}\" ({Ms:F0}ms)",
            result.Channel,
            result.GeneratedText.Length > 40 ? result.GeneratedText[..40] + "..." : result.GeneratedText,
            result.GenerationMs);

        SuggestionReady?.Invoke(this, suggestion);
    }

    // ════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════

    private string GetTargetLine(int lineNumber)
    {
        if (string.IsNullOrEmpty(_currentTargetText)) return string.Empty;
        var lines = _currentTargetText.Split('\n');
        if (lineNumber < 1 || lineNumber > lines.Length) return string.Empty;
        return lines[lineNumber - 1];
    }

    private string GetSourceLine(int lineNumber)
    {
        if (string.IsNullOrEmpty(_currentSourceText)) return string.Empty;
        var lines = _currentSourceText.Split('\n');
        if (lineNumber < 1 || lineNumber > lines.Length) return string.Empty;
        return lines[lineNumber - 1];
    }

    private string GetSourceSentenceForLine(int targetLine)
    {
        // Map target line to source line (1:1 for sentence-level alignment)
        return GetSourceLine(targetLine);
    }

    private void CancelBurstTimer()
    {
        if (_burstCts is not null)
        {
            try { _burstCts.Cancel(); } catch (ObjectDisposedException) { }
            try { _burstCts.Dispose(); } catch (ObjectDisposedException) { }
        }
        _burstCts = null;
    }

    private void CancelPauseTimer()
    {
        if (_pauseCts is not null)
        {
            try { _pauseCts.Cancel(); } catch (ObjectDisposedException) { }
            try { _pauseCts.Dispose(); } catch (ObjectDisposedException) { }
        }
        _pauseCts = null;
    }

    public void Dispose()
    {
        CancelBurstTimer();
        CancelPauseTimer();
        _queueService.GenerationCompleted -= OnGenerationCompleted;
    }
}
