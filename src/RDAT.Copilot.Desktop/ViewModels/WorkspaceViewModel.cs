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
/// </summary>
public partial class WorkspaceViewModel : ObservableObject
{
    private readonly ILogger<WorkspaceViewModel> _logger;
    private readonly IRagPipelineService? _ragPipeline;
    private readonly ILocalInferenceService? _inferenceService;
    private readonly ILlmQueueService? _queueService;
    private readonly IGhostTextCoordinator? _coordinator;

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

    [ObservableProperty]
    private string _grammarState = "Clean";

    [ObservableProperty]
    private int _grammarErrorCount;

    [ObservableProperty]
    private int _grammarWarningCount;

    // ─── Phase 3: Ghost Text State ─────────────────────────────────

    [ObservableProperty]
    private string _activeChannel = "None";

    [ObservableProperty]
    private string _lastSuggestionChannel = string.Empty;

    [ObservableProperty]
    private int _queueDepth;

    [ObservableProperty]
    private string _llmModelName = string.Empty;

    private readonly SourceEditorViewModel _sourceEditor;
    private readonly TargetEditorViewModel _targetEditor;

    public SourceEditorViewModel SourceEditor => _sourceEditor;
    public TargetEditorViewModel TargetEditor => _targetEditor;

    public WorkspaceViewModel(
        SourceEditorViewModel sourceEditor,
        TargetEditorViewModel targetEditor,
        IRagPipelineService? ragPipeline,
        ILocalInferenceService? inferenceService,
        ILlmQueueService? queueService,
        IGhostTextCoordinator? coordinator,
        ILogger<WorkspaceViewModel> logger)
    {
        _sourceEditor = sourceEditor;
        _targetEditor = targetEditor;
        _ragPipeline = ragPipeline;
        _inferenceService = inferenceService;
        _queueService = queueService;
        _coordinator = coordinator;
        _logger = logger;

        // Set default source text
        _sourceText = """
            The Great Pyramid of Giza is the oldest and largest of the three pyramids in the Giza pyramid complex bordering present-day Giza in Greater Cairo, Egypt. It is the oldest of the Seven Wonders of the Ancient World, and the only one to remain largely intact.

            The Great Pyramid was built as a tomb for the Fourth Dynasty pharaoh Khufu, also known by his Greek name Cheops. Construction of the pyramid is thought to have taken approximately twenty years, employing a workforce of around 100,000 skilled laborers and craftsmen.
            """;

        _logger.LogInformation("[RDAT] WorkspaceViewModel initialized (EN→AR)");

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
    /// Updates state and forwards to the coordinator.
    /// </summary>
    public async Task OnTargetTextChangedAsync(string fullText)
    {
        TargetText = fullText;

        if (_coordinator is not null)
        {
            await _coordinator.OnTargetTextChangedAsync(fullText).ConfigureAwait(true);
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
        HasRagMatch = false;
        RagMatchText = string.Empty;
        LastSuggestionChannel = string.Empty;
        ActiveChannel = "None";
        _logger.LogInformation("[RDAT] Workspace cleared");
    }
}
