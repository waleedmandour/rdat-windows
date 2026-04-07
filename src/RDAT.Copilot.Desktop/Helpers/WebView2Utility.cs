using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;

namespace RDAT.Copilot.Desktop.Helpers;

/// <summary>
/// Utility methods for WebView2 initialization and configuration.
/// </summary>
public static class WebView2Utility
{
    /// <summary>
    /// Ensures the CoreWebView2 is initialized for the given WebView2 control.
    /// Sets up the WebView2 environment with default settings.
    /// </summary>
    public static async Task EnsureCoreWebView2Async(
        WebView2 webView,
        string? userDataFolder = null,
        ILogger? logger = null)
    {
        if (webView.CoreWebView2 is not null)
            return;

        var envOptions = new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = "--disable-web-security --allow-file-access-from-files"
        };

        var environment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment
            .CreateWithOptionsAsync(null, userDataFolder, envOptions);

        await webView.EnsureCoreWebView2Async(environment);

        logger?.LogDebug("[RDAT-WebView2] CoreWebView2 initialized successfully");
    }
}
