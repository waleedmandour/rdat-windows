using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Desktop.ViewModels;

/// <summary>
/// ViewModel for the target (active translation) editor pane.
/// Tracks cursor position, manages grammar check state, and coordinates
/// with the WebView2 bridge to receive ghost text and apply markers.
/// </summary>
public partial class TargetEditorViewModel : ObservableObject
{
    private readonly ILogger<TargetEditorViewModel> _logger;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private int _cursorLine = 1;

    [ObservableProperty]
    private int _cursorColumn = 1;

    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private bool _isGrammarChecking;

    [ObservableProperty]
    private string _grammarStatus = "idle";

    [ObservableProperty]
    private int _spellingErrorCount;

    [ObservableProperty]
    private int _grammarWarningCount;

    [ObservableProperty]
    private string? _pauseSuggestion;

    [ObservableProperty]
    private string? _burstSuggestion;

    public TargetEditorViewModel(ILogger<TargetEditorViewModel> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the text content of the line at the cursor position.
    /// Used by Channel 5 (Burst) and Channel 6 (Pause) for context.
    /// </summary>
    public string GetCurrentLineText()
    {
        if (string.IsNullOrEmpty(Text)) return string.Empty;

        var lines = Text.Split('\n');
        if (CursorLine < 1 || CursorLine > lines.Length) return string.Empty;

        return lines[CursorLine - 1];
    }

    /// <summary>
    /// Gets the text content of the line above the cursor (previous line).
    /// Used by Channel 6 (Pause) for register continuity.
    /// </summary>
    public string GetPreviousLineText()
    {
        if (string.IsNullOrEmpty(Text)) return string.Empty;

        var lines = Text.Split('\n');
        var prevLine = CursorLine - 2; // 1-based cursor - 2 = previous line index
        if (prevLine < 0 || prevLine >= lines.Length) return string.Empty;

        return lines[prevLine];
    }
}
