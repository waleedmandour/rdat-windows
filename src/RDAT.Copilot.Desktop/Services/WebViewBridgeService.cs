using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using RDAT.Copilot.Desktop.Helpers;

namespace RDAT.Copilot.Desktop.Services;

/// <summary>
/// Implements the C# <-> JavaScript interop bridge using CoreWebView2.
/// 
/// Protocol:
///   C# → JS: PostWebMessageAsJson({ "type": "command", "command": "...", "payload": {...} })
///   JS → C#: window.chrome.webview.postMessage({ "type": "event", "event": "...", "data": {...} })
///
/// Events received from JS:
///   - cursorPositionChanged: { lineNumber, column }
///   - textChanged: { text }
///   - completionAccepted: { text }
///
/// Commands sent to JS:
///   - setText: { text }
///   - triggerInlineSuggest: {}
///   - applyMarkers: { owner, markers[] }
///   - clearMarkers: { owner }
///   - getText: {}
/// </summary>
public partial class WebViewBridgeService : ObservableObject, IWebViewBridge
{
    private readonly ILogger<WebViewBridgeService> _logger;
    private readonly Dictionary<string, WebView2> _webViews = new();
    private readonly Dictionary<string, TaskCompletionSource<string>> _pendingGetText = new();

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public WebViewBridgeService(ILogger<WebViewBridgeService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task InitializeAsync(WebView2 webView, string paneId)
    {
        // Wait for WebView2 to initialize
        await webView.EnsureCoreWebView2Async();

        _webViews[paneId] = webView;

        // Register message handler for JS → C# communication
        webView.CoreWebView2.WebMessageReceived += (sender, args) =>
        {
            HandleWebMessage(paneId, args.TryGetWebMessageAsString());
        };

        _logger.LogInformation("[RDAT-Bridge] WebView2 initialized for pane: {PaneId}", paneId);
    }

    /// <summary>
    /// Handles incoming messages from JavaScript (JS → C#).
    /// Dispatches typed events to the appropriate handler.
    /// </summary>
    private void HandleWebMessage(string paneId, string? messageJson)
    {
        if (string.IsNullOrEmpty(messageJson)) return;

        try
        {
            var message = JsonNode.Parse(messageJson);
            if (message is null) return;

            var type = message["type"]?.GetValue<string>() ?? string.Empty;
            var eventData = message["data"];

            switch (type)
            {
                case "event":
                    HandleEvent(paneId, message["event"]?.GetValue<string>() ?? string.Empty, eventData);
                    break;

                case "response":
                    HandleResponse(paneId, message);
                    break;

                default:
                    _logger.LogWarning("[RDAT-Bridge] Unknown message type from {PaneId}: {Type}", paneId, type);
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[RDAT-Bridge] Failed to parse message from {PaneId}", paneId);
        }
    }

    /// <summary>
    /// Dispatches a typed event from JS to the appropriate handler.
    /// </summary>
    private void HandleEvent(string paneId, string eventType, JsonNode? data)
    {
        switch (eventType)
        {
            case "cursorPositionChanged":
                var line = data?["lineNumber"]?.GetValue<int>() ?? 1;
                var col = data?["column"]?.GetValue<int>() ?? 1;
                _logger.LogDebug("[RDAT-Bridge] Cursor ({PaneId}): L{Line}:C{Col}", paneId, line, col);
                // Phase 3: Dispatch to WorkspaceViewModel via IMessenger
                break;

            case "textChanged":
                var text = data?["text"]?.GetValue<string>() ?? string.Empty;
                _logger.LogDebug("[RDAT-Bridge] Text changed ({PaneId}): {Length} chars", paneId, text.Length);
                // Phase 3: Dispatch to WorkspaceViewModel via IMessenger
                break;

            case "completionAccepted":
                var acceptedText = data?["text"]?.GetValue<string>() ?? string.Empty;
                _logger.LogInformation("[RDAT-Bridge] Completion accepted ({PaneId}): {Length} chars", paneId, acceptedText.Length);
                break;

            default:
                _logger.LogDebug("[RDAT-Bridge] Unhandled event ({PaneId}): {Event}", paneId, eventType);
                break;
        }
    }

    /// <summary>
    /// Handles response messages (e.g., getText responses).
    /// </summary>
    private void HandleResponse(string paneId, JsonNode message)
    {
        var requestId = message["requestId"]?.GetValue<string>();
        var payload = message["payload"]?["text"]?.GetValue<string>();

        if (requestId is not null && _pendingGetText.TryGetValue(requestId, out var tcs))
        {
            _pendingGetText.Remove(requestId);
            tcs.TrySetResult(payload ?? string.Empty);
        }
    }

    /// <inheritdoc/>
    public async Task PostCommandAsync(string paneId, string commandType, object? payload = null)
    {
        if (!_webViews.TryGetValue(paneId, out var webView)) return;
        if (webView.CoreWebView2 is null) return;

        var message = new
        {
            type = "command",
            command = commandType,
            payload = payload
        };

        var json = JsonSerializer.Serialize(message, _jsonOptions);
        webView.CoreWebView2.PostWebMessageAsJson(json);

        _logger.LogDebug("[RDAT-Bridge] Command → {PaneId}: {Command}", paneId, commandType);
    }

    /// <inheritdoc/>
    public async Task SetTextAsync(string paneId, string text)
    {
        await PostCommandAsync(paneId, "setText", new { text });
    }

    /// <inheritdoc/>
    public async Task TriggerInlineSuggestAsync(string paneId)
    {
        await PostCommandAsync(paneId, "triggerInlineSuggest");
    }

    /// <inheritdoc/>
    public async Task ApplyMarkersAsync(string paneId, object[] markers)
    {
        await PostCommandAsync(paneId, "applyMarkers", new { owner = "rdat-grammar", markers });
    }

    /// <inheritdoc/>
    public async Task ClearMarkersAsync(string paneId, string owner)
    {
        await PostCommandAsync(paneId, "clearMarkers", new { owner });
    }

    /// <inheritdoc/>
    public async Task<string> GetTextAsync(string paneId)
    {
        if (!_webViews.TryGetValue(paneId, out var webView)) return string.Empty;
        if (webView.CoreWebView2 is null) return string.Empty;

        var requestId = Guid.NewGuid().ToString("N")[..8];
        var tcs = new TaskCompletionSource<string>();
        _pendingGetText[requestId] = tcs;

        await PostCommandAsync(paneId, "getText", new { requestId });

        // Timeout after 5 seconds
        var timeoutTask = Task.Delay(5000);
        var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

        if (completedTask == timeoutTask)
        {
            _pendingGetText.Remove(requestId);
            _logger.LogWarning("[RDAT-Bridge] getText timed out for {PaneId}", paneId);
            return string.Empty;
        }

        return await tcs.Task;
    }
}
