using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace RDAT.Copilot.Desktop.ViewModels;

/// <summary>
/// ViewModel for the source (read-only) editor pane.
/// Manages source text state and provides the current source
/// sentence for RAG lookups and context-aware generation.
/// </summary>
public partial class SourceEditorViewModel : ObservableObject
{
    private readonly ILogger<SourceEditorViewModel> _logger;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private bool _isEditingSource;

    [ObservableProperty]
    private string _editSourceDraft = string.Empty;

    public SourceEditorViewModel(ILogger<SourceEditorViewModel> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Extracts the source sentence corresponding to a given line number.
    /// Used by the RAG pipeline and grammar checker for context-aware analysis.
    /// </summary>
    public string GetSourceSentence(int lineNumber)
    {
        if (string.IsNullOrEmpty(Text)) return string.Empty;

        var lines = Text.Split('\n');
        if (lineNumber < 1 || lineNumber > lines.Length) return string.Empty;

        return lines[lineNumber - 1].Trim();
    }
}
