namespace RDAT.Copilot.Desktop.Services;

/// <summary>
/// Interface for the C# <-> JavaScript WebView2 interop bridge.
/// Provides typed methods for sending commands to Monaco Editor
/// and receiving structured events from the editor.
/// </summary>
public interface IWebViewBridge
{
    /// <summary>
    /// Initializes the WebView2 control and registers message handlers.
    /// </summary>
    Task InitializeAsync(Microsoft.UI.Xaml.Controls.WebView2 webView, string paneId);

    /// <summary>
    /// Sends a command to the JavaScript side of the specified pane.
    /// </summary>
    Task PostCommandAsync(string paneId, string commandType, object? payload = null);

    /// <summary>
    /// Sets the text content of an editor pane.
    /// </summary>
    Task SetTextAsync(string paneId, string text);

    /// <summary>
    /// Triggers Monaco to show ghost text inline completions.
    /// </summary>
    Task TriggerInlineSuggestAsync(string paneId);

    /// <summary>
    /// Applies grammar/spell markers to the target editor.
    /// </summary>
    Task ApplyMarkersAsync(string paneId, object[] markers);

    /// <summary>
    /// Clears all grammar markers from the target editor.
    /// </summary>
    Task ClearMarkersAsync(string paneId, string owner);

    /// <summary>
    /// Gets the current text content from an editor pane.
    /// </summary>
    Task<string> GetTextAsync(string paneId);

    /// <summary>
    /// Send a RAG (TM match) ghost text suggestion to the target editor.
    /// </summary>
    Task SetRagSuggestionAsync(string text, double score);

    /// <summary>
    /// Clear the RAG suggestion from the target editor.
    /// </summary>
    Task ClearRagSuggestionAsync();

    /// <summary>
    /// Highlight a specific line in the source editor.
    /// </summary>
    Task HighlightSourceLineAsync(int lineNumber);

    /// <summary>
    /// Send grammar error markers to the target editor.
    /// </summary>
    Task SetGrammarMarkersAsync(object[] markers);

    /// <summary>
    /// Clear all grammar markers from the target editor.
    /// </summary>
    Task ClearGrammarMarkersAsync();

    /// <summary>
    /// Apply a quick fix to the target editor.
    /// </summary>
    Task ApplyQuickFixAsync(int lineNumber, int startColumn, int endColumn, string newText);

    /// <summary>
    /// Send AMTA terminology lint markers to the target editor.
    /// </summary>
    Task SetAmtaLintMarkersAsync(object[] markers);

    /// <summary>
    /// Clear all AMTA lint markers from the target editor.
    /// </summary>
    Task ClearAmtaLintMarkersAsync();
}
