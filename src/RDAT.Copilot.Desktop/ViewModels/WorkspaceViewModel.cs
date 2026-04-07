using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Desktop.ViewModels;

/// <summary>
/// Root ViewModel for the WorkspacePage. Orchestrates source and target
/// editor ViewModels, manages language direction state, and coordinates
/// the Tri-Channel Ghost Text Architecture from the MVVM layer.
/// </summary>
public partial class WorkspaceViewModel : ObservableObject
{
    private readonly ILogger<WorkspaceViewModel> _logger;

    [ObservableProperty]
    private LanguageDirection _languageDirection = LanguageDirection.EnToAr;

    [ObservableProperty]
    private string _sourceText = string.Empty;

    [ObservableProperty]
    private string _targetText = string.Empty;

    [ObservableProperty]
    private int _activeTargetLine = 1;

    [ObservableProperty]
    private bool _isLLMReady;

    [ObservableProperty]
    private string _llmState = "Idle";

    [ObservableProperty]
    private string _ragState = "GTR: Idle";

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
        ILogger<WorkspaceViewModel> logger)
    {
        _sourceEditor = sourceEditor;
        _targetEditor = targetEditor;
        _logger = logger;

        // Set default source text
        _sourceText = """
            The Great Pyramid of Giza is the oldest and largest of the three pyramids in the Giza pyramid complex bordering present-day Giza in Greater Cairo, Egypt. It is the oldest of the Seven Wonders of the Ancient World, and the only one to remain largely intact.

            The Great Pyramid was built as a tomb for the Fourth Dynasty pharaoh Khufu, also known by his Greek name Cheops. Construction of the pyramid is thought to have taken approximately twenty years, employing a workforce of around 100,000 skilled laborers and craftsmen.
            """;

        _logger.LogInformation("[RDAT] WorkspaceViewModel initialized (EN→AR)");
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
        _logger.LogInformation("[RDAT] Workspace cleared");
    }
}
