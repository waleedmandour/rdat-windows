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
}
