using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Desktop.ViewModels;

/// <summary>
/// Root ViewModel for the WorkspacePage. Orchestrates source and target
/// editor ViewModels, manages language direction state, and coordinates
/// the Four-Channel Ghost Text Architecture from the MVVM layer.
/// Phase 2: Integrates RAG pipeline for TM-based ghost text (GTR channel).
/// Phase 3: Integrates LLM queue engine for Burst/Pause/Prefetch channels.
/// Phase 4: Integrates grammar checker, AMTA linter, and Gemini cloud service.
/// </summary>
public partial class WorkspaceViewModel : ObservableObject
{
    private readonly ILogger<WorkspaceViewModel> _logger;
    private readonly IRagPipelineService? _ragPipeline;
    private readonly ILocalInferenceService? _inferenceService;
    private readonly ILlmQueueService? _queueService;
    private readonly IGhostTextCoordinator? _coordinator;
    private readonly IGrammarCheckerService? _grammarChecker;
    private readonly IAmtaLinterService? _amtaLinter;
    private readonly IGeminiCloudService? _geminiService;

    [ObservableProperty]
    private LanguageDirection _languageDirection = LanguageDirection.EnToAr;

    [ObservableProperty]
    private string _sourceText = string.Empty;

    [ObservableProperty]
    private string _targetText = string.Empty;

    [ObservableProperty]
    private int _activeTargetLine = 1;

    [ObservableProperty]
    private int _activeSourceLine = 1;

    [ObservableProperty]
    private bool _isLLMReady;

    [ObservableProperty]
    private string _llmState = "Idle";

    [ObservableProperty]
    private string _ragState = "GTR: Idle";

    [ObservableProperty]
    private string _ragDetail = string.Empty;

    [ObservableProperty]
    private bool _hasRagMatch;

    [ObservableProperty]
    private string _ragMatchText = string.Empty;

    [ObservableProperty]
    private double _ragMatchScore;

    // ─── Phase 4: Grammar Checker State ──────────────────────────

    [ObservableProperty]
    private string _grammarState = "Clean";

    [ObservableProperty]
    private int _grammarErrorCount;

    [ObservableProperty]
    private int _grammarWarningCount;

    [ObservableProperty]
    private bool _isGrammarEnabled = true;

    // ─── Phase 4: AMTA Linter State ──────────────────────────────

    [ObservableProperty]
    private string _amtaState = "No Glossary";

    [ObservableProperty]
    private int _amtaTermCount;

    [ObservableProperty]
    private int _amtaIssueCount;

    [ObservableProperty]
    private bool _isAmtaLintEnabled = true;

    // ─── Phase 4: Gemini Cloud State ─────────────────────────────

    [ObservableProperty]
    private string _geminiStateDisplay = "Not Configured";

    [ObservableProperty]
    private bool _isGeminiReady;

    // ─── Phase 3: Ghost Text State ─────────────────────────────────

    [ObservableProperty]
    private string _activeChannel = "None";

    [ObservableProperty]
    private string _lastSuggestionChannel = string.Empty;

    [ObservableProperty]
    private int _queueDepth;

    [ObservableProperty]
    private string _llmModelName = string.Empty;

    // ─── Phase 4: Quality Estimation ─────────────────────────────

    [ObservableProperty]
    private int _qualityScore;

    [ObservableProperty]
    private string _qualityFeedback = string.Empty;

    // ─── Phase 4: Grammar/Lint Issues Collections ────────────────

    private List<GrammarIssue> _grammarIssues = new();
    private List<AmtaLintIssue> _amtaIssues = new();

    /// <summary>Grammar issues for the error list UI binding.</summary>
    public IReadOnlyList<GrammarIssue> GrammarIssues => _grammarIssues;

    /// <summary>AMTA lint issues for the lint panel UI binding.</summary>
    public IReadOnlyList<AmtaLintIssue> AmtaIssues => _amtaIssues;

    private readonly SourceEditorViewModel _sourceEditor;
    private readonly TargetEditorViewModel _targetEditor;

    // Debounce timers for grammar and AMTA lint
    private CancellationTokenSource? _grammarCts;
    private CancellationTokenSource? _amtaCts;

    public SourceEditorViewModel SourceEditor => _sourceEditor;
    public TargetEditorViewModel TargetEditor => _targetEditor;

    public WorkspaceViewModel(
        SourceEditorViewModel sourceEditor,
        TargetEditorViewModel targetEditor,
        IRagPipelineService? ragPipeline,
        ILocalInferenceService? inferenceService,
        ILlmQueueService? queueService,
        IGhostTextCoordinator? coordinator,
        IGrammarCheckerService? grammarChecker,
        IAmtaLinterService? amtaLinter,
        IGeminiCloudService? geminiService,
        ILogger<WorkspaceViewModel> logger)
    {
        _sourceEditor = sourceEditor;
        _targetEditor = targetEditor;
        _ragPipeline = ragPipeline;
        _inferenceService = inferenceService;
        _queueService = queueService;
        _coordinator = coordinator;
        _grammarChecker = grammarChecker;
        _amtaLinter = amtaLinter;
        _geminiService = geminiService;
        _logger = logger;

        // Set default source text
        _sourceText = """
            The Great Pyramid of Giza is the oldest and largest of the three pyramids in the Giza pyramid complex bordering present-day Giza in Greater Cairo, Egypt. It is the oldest of the Seven Wonders of the Ancient World, and the only one to remain largely intact.

            The Great Pyramid was built as a tomb for the Fourth Dynasty pharaoh Khufu, also known by his Greek name Cheops. Construction of the pyramid is thought to have taken approximately twenty years, employing a workforce of around 100,000 skilled laborers and craftsmen.
            """;

        _logger.LogInformation("[RDAT] WorkspaceViewModel initialized (EN→AR) — Phase 4");

        // Subscribe to RAG pipeline state changes
        if (_ragPipeline is not null)
        {
            UpdateRagState();
        }

        // Subscribe to LLM state changes
        if (_inferenceService is not null)
        {
            UpdateLlmState();
        }

        // Subscribe to coordinator suggestion events
        if (_coordinator is not null)
        {
            _coordinator.SuggestionReady += OnSuggestionReady;
        }

        // Subscribe to AMTA linter events
        if (_amtaLinter is not null)
        {
            _amtaLinter.LintCompleted += OnAmtaLintCompleted;
            UpdateAmtaState();
        }

        // Subscribe to Gemini state changes
        if (_geminiService is not null)
        {
            _geminiService.StateChanged += OnGeminiStateChanged;
            UpdateGeminiState();
        }
    }

    /// <summary>
    /// Update LLM state display from the inference service.
    /// </summary>
    private void UpdateLlmState()
    {
        if (_inferenceService is null) return;

        IsLLMReady = _inferenceService.IsReady;
        LlmState = _inferenceService.State.ToString();
        ActiveChannel = _inferenceService.State == LlmState.Generating
            ? "Generating..." : "None";

        _logger.LogInformation("[RDAT] LLM state updated: {State}, Ready: {Ready}",
            _inferenceService.State, _inferenceService.IsReady);
    }

    /// <summary>
    /// Update RAG state display from the pipeline.
    /// </summary>
    private void UpdateRagState()
    {
        if (_ragPipeline is null) return;

        RAGState = $"GTR: {_ragPipeline.State}";
        HasRagMatch = false;

        _logger.LogInformation("[RDAT] RAG state updated: {State}, TM count: {Count}",
            _ragPipeline.State, _ragPipeline.TotalTmCount);
    }

    /// <summary>
    /// Update AMTA linter state display.
    /// </summary>
    private void UpdateAmtaState()
    {
        if (_amtaLinter is null) return;

        AmtaState = _amtaLinter.State switch
        {
            AmtaLinterState.Idle => "No Glossary",
            AmtaLinterState.Loading => "Loading...",
            AmtaLinterState.Ready => $"Ready ({_amtaLinter.TermCount} terms)",
            AmtaLinterState.Checking => "Checking...",
            AmtaLinterState.Error => "Error",
            _ => "Unknown"
        };

        AmtaTermCount = _amtaLinter.TermCount;

        _logger.LogDebug("[RDAT] AMTA state: {State}", AmtaState);
    }

    /// <summary>
    /// Update Gemini state display.
    /// </summary>
    private void UpdateGeminiState()
    {
        if (_geminiService is null) return;

        GeminiStateDisplay = _geminiService.State switch
        {
            GeminiState.NotConfigured => "Not Configured",
            GeminiState.Configured => "Key Set",
            GeminiState.Ready => "Connected ✓",
            GeminiState.Busy => "Processing...",
            GeminiState.Error => "Error ✗",
            _ => "Unknown"
        };

        IsGeminiReady = _geminiService.State == GeminiState.Ready;

        _logger.LogDebug("[RDAT] Gemini state: {State}", GeminiStateDisplay);
    }

    /// <summary>
    /// Update queue depth from the queue service.
    /// </summary>
    public void UpdateQueueDepth()
    {
        if (_queueService is not null)
        {
            QueueDepth = _queueService.PendingCount;
        }
    }

    /// <summary>
    /// Called when the GhostTextCoordinator produces a suggestion.
    /// Routes it to the WebViewBridge for display in Monaco.
    /// </summary>
    private void OnSuggestionReady(object? sender, GhostTextSuggestion suggestion)
    {
        LastSuggestionChannel = suggestion.Channel;
        ActiveChannel = suggestion.Channel;

        _logger.LogInformation(
            "[RDAT] Ghost text suggestion: {Channel} — \"{Text}\"",
            suggestion.Channel,
            suggestion.InsertText.Length > 40 ? suggestion.InsertText[..40] + "..." : suggestion.InsertText);
    }

    /// <summary>
    /// Called when the AMTA linter completes a check.
    /// Updates the issues list and UI state.
    /// </summary>
    private void OnAmtaLintCompleted(object? sender, IReadOnlyList<AmtaLintIssue> issues)
    {
        _amtaIssues = issues.ToList();
        AmtaIssueCount = issues.Count;
        UpdateAmtaState();

        // Raise collection change notifications
        OnPropertyChanged(nameof(AmtaIssues));

        _logger.LogInformation(
            "[RDAT] AMTA lint complete: {Count} issues", issues.Count);
    }

    /// <summary>
    /// Called when the Gemini service state changes.
    /// </summary>
    private void OnGeminiStateChanged(object? sender, GeminiState state)
    {
        UpdateGeminiState();
    }

    /// <summary>
    /// Called when the cursor moves in the target editor.
    /// Triggers a RAG search for the corresponding source sentence.
    /// </summary>
    public async Task OnTargetCursorChangedAsync(int lineNumber, int column)
    {
        ActiveTargetLine = lineNumber;

        // Phase 2: RAG lookup
        if (_ragPipeline?.IsReady == true)
        {
            var sourceSentence = SourceEditor.GetSourceSentence(lineNumber);
            if (!string.IsNullOrWhiteSpace(sourceSentence))
            {
                try
                {
                    var bestMatch = await _ragPipeline.GetBestMatchAsync(sourceSentence).ConfigureAwait(true);
                    if (bestMatch is not null && bestMatch.Score >= 0.7)
                    {
                        HasRagMatch = true;
                        RagMatchText = bestMatch.Entry.TargetText;
                        RagMatchScore = bestMatch.Score;
                        RAGDetail = $"GTR: {bestMatch.Score:P0} match";
                    }
                    else
                    {
                        HasRagMatch = false;
                        RAGDetail = $"GTR: {_ragPipeline.State}";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[RDAT] RAG lookup failed for line {Line}", lineNumber);
                }
            }
        }

        // Phase 3: Forward to GhostTextCoordinator for LLM channels
        if (_coordinator is not null)
        {
            await _coordinator.OnTargetCursorChangedAsync(lineNumber, column).ConfigureAwait(true);
        }

        // Update queue depth display
        UpdateQueueDepth();
        UpdateLlmState();
    }

    /// <summary>
    /// Called when the cursor moves in the source editor.
    /// Updates the active source line for RAG context.
    /// </summary>
    public void OnSourceCursorChanged(int lineNumber, int column)
    {
        ActiveSourceLine = lineNumber;
    }

    /// <summary>
    /// Called when the target editor text changes.
    /// Updates state and triggers grammar + AMTA lint checks with debounce.
    /// </summary>
    public async Task OnTargetTextChangedAsync(string fullText)
    {
        TargetText = fullText;

        if (_coordinator is not null)
        {
            await _coordinator.OnTargetTextChangedAsync(fullText).ConfigureAwait(true);
        }

        // Phase 4: Debounced grammar check (2.5s)
        if (_grammarChecker is not null && IsGrammarEnabled)
        {
            CancelGrammarTimer();
            _grammarCts = new CancellationTokenSource();
            var token = _grammarCts.Token;
            var textSnapshot = fullText;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(Constants.AppConstants.GrammarCheckDebounceMs, token).ConfigureAwait(false);
                    if (token.IsCancellationRequested) return;

                    var sourceSentence = GetSourceSentenceForTarget(ActiveTargetLine);
                    var issues = await _grammarChecker.CheckAsync(
                        textSnapshot, sourceSentence, LanguageDirection, token)
                        .ConfigureAwait(true);

                    _grammarIssues = issues.ToList();
                    GrammarErrorCount = issues.Count(i => i.Type is GrammarErrorType.Grammar or GrammarErrorType.Spelling);
                    GrammarWarningCount = issues.Count(i => i.Type is GrammarErrorType.Punctuation or GrammarErrorType.Style);
                    GrammarState = issues.Count == 0 ? "Clean" : $"{issues.Count} issues";
                    OnPropertyChanged(nameof(GrammarIssues));
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[RDAT] Grammar check error");
                }
            }, token);
        }

        // Phase 4: Debounced AMTA lint check (2s)
        if (_amtaLinter is not null && _amtaLinter.IsReady && IsAmtaLintEnabled)
        {
            CancelAmtaTimer();
            _amtaCts = new CancellationTokenSource();
            var token = _amtaCts.Token;
            var textSnapshot = fullText;
            var sourceSnapshot = SourceText;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(Constants.AppConstants.AmtaLintDebounceMs, token).ConfigureAwait(false);
                    if (token.IsCancellationRequested) return;

                    await _amtaLinter.CheckAsync(textSnapshot, sourceSnapshot, token)
                        .ConfigureAwait(true);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[RDAT] AMTA lint error");
                }
            }, token);
        }
    }

    /// <summary>
    /// Called when the source editor text changes.
    /// Forwards to the coordinator for Prefetch channel.
    /// </summary>
    public async Task OnSourceTextChangedAsync(string fullText)
    {
        SourceEditor.Text = fullText;
        SourceText = fullText;

        if (_coordinator is not null)
        {
            await _coordinator.OnSourceTextChangedAsync(fullText).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Run a Gemini quality estimation on the current translation.
    /// </summary>
    [RelayCommand]
    private async Task RunQualityEstimationAsync()
    {
        if (_geminiService is null || !_geminiService.IsConfigured) return;
        if (string.IsNullOrWhiteSpace(TargetText)) return;

        try
        {
            QualityFeedback = "Estimating quality...";
            var result = await _geminiService.EstimateQualityAsync(SourceText, TargetText).ConfigureAwait(true);
            QualityScore = result.OverallScore;
            QualityFeedback = $"Score: {result.OverallScore}/100 | Accuracy: {result.AccuracyScore} | Fluency: {result.FluencyScore} | Style: {result.StyleScore} | Terms: {result.TerminologyScore}";
        }
        catch (Exception ex)
        {
            QualityFeedback = $"Error: {ex.Message}";
            _logger.LogError(ex, "[RDAT] Quality estimation failed");
        }
    }

    /// <summary>
    /// Accept a grammar suggestion and apply it to the target text.
    /// </summary>
    [RelayCommand]
    private void AcceptGrammarSuggestion(GrammarIssue issue)
    {
        // The actual text replacement is handled via WebViewBridge → Monaco
        _logger.LogInformation(
            "[RDAT] Grammar suggestion accepted: {Type} — \"{Suggestion}\"",
            issue.Type, issue.Suggestion);
    }

    /// <summary>
    /// Accept an AMTA lint suggestion and apply it to the target text.
    /// </summary>
    [RelayCommand]
    private void AcceptAmtaSuggestion(AmtaLintIssue issue)
    {
        _logger.LogInformation(
            "[RDAT] AMTA suggestion accepted: {Type} — \"{Suggestion}\"",
            issue.Type, issue.Suggestion);
    }

    /// <summary>
    /// Initialize the LLM engine and start the ghost text coordinator.
    /// Called from SettingsPage after model path configuration.
    /// </summary>
    public async Task InitializeLlmAsync(string modelPath, IProgress<(double Progress, string Text)>? progress = null)
    {
        if (_inferenceService is null) return;

        IsLLMReady = false;
        LlmState = "Initializing...";

        try
        {
            await _inferenceService.InitializeAsync(modelPath, progress).ConfigureAwait(true);
            UpdateLlmState();

            // Start the queue and coordinator
            if (_queueService is not null)
            {
                await _queueService.StartAsync().ConfigureAwait(true);
            }

            if (_coordinator is not null)
            {
                await _coordinator.StartAsync().ConfigureAwait(true);
            }

            _logger.LogInformation("[RDAT] LLM engine initialized and ghost text coordinator started");
        }
        catch (Exception ex)
        {
            LlmState = "Error";
            _logger.LogError(ex, "[RDAT] LLM initialization failed");
        }
    }

    /// <summary>
    /// Load an AMTA glossary file.
    /// Called from SettingsPage after glossary file selection.
    /// </summary>
    public async Task LoadGlossaryAsync(string filePath, IProgress<(double Progress, string Text)>? progress = null)
    {
        if (_amtaLinter is null) return;

        try
        {
            var count = await _amtaLinter.LoadGlossaryAsync(filePath, progress).ConfigureAwait(true);
            UpdateAmtaState();
            _logger.LogInformation("[RDAT] AMTA glossary loaded: {Count} terms from {Path}", count, filePath);
        }
        catch (Exception ex)
        {
            UpdateAmtaState();
            _logger.LogError(ex, "[RDAT] AMTA glossary load failed");
        }
    }

    [RelayCommand]
    private void SwapLanguageDirection()
    {
        LanguageDirection = LanguageDirection == LanguageDirection.EnToAr
            ? LanguageDirection.ArToEn
            : LanguageDirection.EnToAr;

        ActiveTargetLine = 1;
        _logger.LogInformation("[RDAT] Language direction swapped: {Direction}", LanguageDirection);
    }

    [RelayCommand]
    private void ClearWorkspace()
    {
        TargetText = string.Empty;
        ActiveTargetLine = 1;
        GrammarErrorCount = 0;
        GrammarWarningCount = 0;
        GrammarState = "Clean";
        _grammarIssues.Clear();
        OnPropertyChanged(nameof(GrammarIssues));
        HasRagMatch = false;
        RagMatchText = string.Empty;
        AmtaIssueCount = 0;
        _amtaIssues.Clear();
        OnPropertyChanged(nameof(AmtaIssues));
        LastSuggestionChannel = string.Empty;
        ActiveChannel = "None";
        QualityScore = 0;
        QualityFeedback = string.Empty;
        _logger.LogInformation("[RDAT] Workspace cleared");
    }

    private string GetSourceSentenceForTarget(int targetLine)
    {
        if (string.IsNullOrEmpty(SourceText)) return string.Empty;
        var lines = SourceText.Split('\n');
        if (targetLine >= 1 && targetLine <= lines.Length)
            return lines[targetLine - 1].Trim();
        return string.Empty;
    }

    private void CancelGrammarTimer()
    {
        try { _grammarCts?.Cancel(); } catch { }
        try { _grammarCts?.Dispose(); } catch { }
        _grammarCts = null;
    }

    private void CancelAmtaTimer()
    {
        try { _amtaCts?.Cancel(); } catch { }
        try { _amtaCts?.Dispose(); } catch { }
        _amtaCts = null;
    }
}
