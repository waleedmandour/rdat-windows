// ========================================================================
// RDAT Copilot - Ghost Text Coordinator
// Location: src/RDAT.Copilot.Core/Services/GhostTextCoordinator.cs
// ========================================================================

using System.Reactive.Linq;
using System.Reactive.Subjects;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Core.Services;

/// <summary>
/// Coordinates the ghost text prediction pipeline. Manages preemption,
/// debouncing, and distributes predictions to subscribers.
/// Falls back to Gemini cloud API when local ONNX inference is unavailable.
/// </summary>
public sealed class GhostTextCoordinator : IDisposable
{
    private readonly ILlmInferenceService _llmService;
    private readonly IAmtaLinterService _linterService;
    private readonly IGeminiService? _geminiService;
    private readonly Subject<GhostTextResult> _resultSubject = new();
    private readonly Subject<EditorKeystroke> _keystrokeSubject = new();

    private CancellationTokenSource? _currentCts;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(300);
    private DateTimeOffset _lastKeystroke;
    private Timer? _debounceTimer;
    private EditorKeystroke? _pendingKeystroke;

    /// <summary>Observable stream of ghost text predictions.</summary>
    public IObservable<GhostTextResult> GhostTextStream =>
        _resultSubject.AsObservable();

    /// <summary>Observable stream of keystroke events from the editor.</summary>
    public IObservable<EditorKeystroke> KeystrokeStream =>
        _keystrokeSubject.AsObservable();

    /// <summary>True when a prediction is currently being generated.</summary>
    public bool IsGenerating { get; private set; }

    public GhostTextCoordinator(
        ILlmInferenceService llmService,
        IAmtaLinterService linterService)
        : this(llmService, linterService, null)
    {
    }

    public GhostTextCoordinator(
        ILlmInferenceService llmService,
        IAmtaLinterService linterService,
        IGeminiService? geminiService)
    {
        _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
        _linterService = linterService ?? throw new ArgumentNullException(nameof(linterService));
        _geminiService = geminiService;

        _debounceTimer = new Timer(async _ =>
        {
            if (DateTimeOffset.UtcNow - _lastKeystroke >= _debounceInterval)
                await GeneratePredictionAsync();
        }, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void OnKeystroke(EditorKeystroke keystroke)
    {
        _keystrokeSubject.OnNext(keystroke);
        _pendingKeystroke = keystroke;
        _lastKeystroke = DateTimeOffset.UtcNow;
        _debounceTimer?.Change((int)_debounceInterval.TotalMilliseconds, Timeout.Infinite);
    }

    public void RequestPrediction(string context, string cursorPrefix)
    {
        OnKeystroke(new EditorKeystroke
        {
            SourceText = context,
            TargetText = cursorPrefix,
            Language = "en", IsRtl = false
        });
    }

    public void CancelPending()
    {
        Interlocked.Exchange(ref _currentCts, null)?.Cancel();
        IsGenerating = false;
    }

    private async Task GeneratePredictionAsync()
    {
        if (_pendingKeystroke is null) return;
        if (string.IsNullOrWhiteSpace(_pendingKeystroke.SourceText)) return;

        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _currentCts, cts)?.Cancel();
        _currentCts = cts;
        var keystroke = _pendingKeystroke;
        _pendingKeystroke = null;

        await _semaphore.WaitAsync(cts.Token);
        try
        {
            IsGenerating = true;
            GhostTextResult result;

            // Try local ONNX inference first
            if (_llmService.IsModelLoaded)
            {
                result = await _llmService.GetPredictionAsync(
                    keystroke.SourceText, "en", "ar", cts.Token);
            }
            // Fall back to Gemini cloud API if configured
            else if (_geminiService?.IsConfigured == true)
            {
                result = await _geminiService.GetPredictionAsync(
                    keystroke.SourceText, "en", "ar", cts.Token);
            }
            else
            {
                // No inference available
                IsGenerating = false;
                return;
            }

            if (!string.IsNullOrEmpty(result.Text))
            {
                string predictionText = result.Text;
                var lintResult = _linterService.LintAndCorrect(ref predictionText);

                result = lintResult.ShouldSuppress
                    ? result with { Text = "", IsSuppressed = true }
                    : lintResult.CorrectedText is not null
                        ? result with { Text = lintResult.CorrectedText }
                        : result;
            }

            _resultSubject.OnNext(result);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
        finally
        {
            IsGenerating = false;
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _currentCts, null)?.Dispose();
        _debounceTimer?.Dispose();
        _resultSubject.Dispose();
        _keystrokeSubject.Dispose();
        _semaphore.Dispose();
    }
}
