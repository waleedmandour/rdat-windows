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
/// the Tri-Channel Ghost Text Architecture from the MVVM layer.
/// Phase 2: Integrates RAG pipeline for TM-based ghost text (GTR channel).
/// </summary>
public partial class WorkspaceViewModel : ObservableObject
{
    private readonly ILogger<WorkspaceViewModel> _logger;
    private readonly IRagPipelineService? _ragPipeline;

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

    private readonly SourceEditorViewModel _sourceEditor;
    private readonly TargetEditorViewModel _targetEditor;

    public SourceEditorViewModel SourceEditor => _sourceEditor;
    public TargetEditorViewModel TargetEditor => _targetEditor;

    public WorkspaceViewModel(
        SourceEditorViewModel sourceEditor,
        TargetEditorViewModel targetEditor,
        IRagPipelineService? ragPipeline,
        ILogger<WorkspaceViewModel> logger)
    {
        _sourceEditor = sourceEditor;
        _targetEditor = targetEditor;
        _ragPipeline = ragPipeline;
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
    /// Called when the cursor moves in the target editor.
    /// Triggers a RAG search for the corresponding source sentence.
    /// </summary>
    public async Task OnTargetCursorChangedAsync(int lineNumber, int column)
    {
        ActiveTargetLine = lineNumber;

        if (_ragPipeline?.IsReady != true) return;

        // Get the corresponding source sentence for RAG lookup
        var sourceSentence = SourceEditor.GetSourceSentence(lineNumber);
        if (string.IsNullOrWhiteSpace(sourceSentence)) return;

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

    /// <summary>
    /// Called when the cursor moves in the source editor.
    /// Updates the active source line for RAG context.
    /// </summary>
    public void OnSourceCursorChanged(int lineNumber, int column)
    {
        ActiveSourceLine = lineNumber;
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
        _logger.LogInformation("[RDAT] Workspace cleared");
    }
}
