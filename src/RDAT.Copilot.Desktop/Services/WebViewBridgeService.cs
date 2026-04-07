using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.Messaging;
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
///   - grammarFixApplied: { issueId, oldText, newText, line }
///
/// Commands sent to JS:
///   - setText: { text }
///   - triggerInlineSuggest: {}
///   - applyMarkers: { owner, markers[] }
///   - clearMarkers: { owner }
///   - getText: {}
///   - setRagSuggestion: { text, score }  (Phase 2: GTR ghost text)
///   - clearRagSuggestion: {}
///   - setGrammarMarkers: { markers[] }    (Phase 4: Grammar error markers)
///   - clearGrammarMarkers: {}             (Phase 4: Clear grammar markers)
///   - applyQuickFix: { line, startCol, endCol, newText } (Phase 4: Quick fix)
/// </summary>
public class WebViewBridgeService : IWebViewBridge
{
    private readonly ILogger<WebViewBridgeService> _logger;
    private readonly ConcurrentDictionary<string, WebView2> _webViews = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingGetText = new();

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
        await WebView2Utility.EnsureCoreWebView2Async(webView, logger: _logger);

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
    /// Phase 2: Dispatches cursor/text events to WorkspaceViewModel via WeakReferenceMessenger.
    /// Phase 4: Dispatches grammar fix applied events.
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
    /// Phase 2: Forwards cursor/text events to the WorkspaceViewModel.
    /// Phase 4: Forwards grammar fix applied events.
    /// </summary>
    private void HandleEvent(string paneId, string eventType, JsonNode? data)
    {
        switch (eventType)
        {
            case "cursorPositionChanged":
                var line = data?["lineNumber"]?.GetValue<int>() ?? 1;
                var col = data?["column"]?.GetValue<int>() ?? 1;
                _logger.LogDebug("[RDAT-Bridge] Cursor ({PaneId}): L{Line}:C{Col}", paneId, line, col);

                // Phase 2: Forward cursor events to WorkspaceViewModel
                if (paneId == "target")
                {
                    WeakReferenceMessenger.Default.Send(new TargetCursorChangedMessage(line, col));
                }
                else if (paneId == "source")
                {
                    WeakReferenceMessenger.Default.Send(new SourceCursorChangedMessage(line, col));
                }
                break;

            case "textChanged":
                var text = data?["text"]?.GetValue<string>() ?? string.Empty;
                _logger.LogDebug("[RDAT-Bridge] Text changed ({PaneId}): {Length} chars", paneId, text.Length);

                // Phase 2: Forward text events to WorkspaceViewModel
                if (paneId == "target")
                {
                    WeakReferenceMessenger.Default.Send(new TargetTextChangedMessage(text));
                }
                else if (paneId == "source")
                {
                    WeakReferenceMessenger.Default.Send(new SourceTextChangedMessage(text));
                }
                break;

            case "completionAccepted":
                var acceptedText = data?["text"]?.GetValue<string>() ?? string.Empty;
                _logger.LogInformation("[RDAT-Bridge] Completion accepted ({PaneId}): {Length} chars", paneId, acceptedText.Length);
                break;

            // Phase 4: Grammar quick fix applied
            case "grammarFixApplied":
                var issueId = data?["issueId"]?.GetValue<string>() ?? string.Empty;
                var oldText = data?["oldText"]?.GetValue<string>() ?? string.Empty;
                var newText = data?["newText"]?.GetValue<string>() ?? string.Empty;
                var fixLine = data?["line"]?.GetValue<int>() ?? 0;
                _logger.LogInformation(
                    "[RDAT-Bridge] Grammar fix applied: {IssueId} — \"{Old}\" → \"{New}\" at L{Line}",
                    issueId, oldText, newText, fixLine);
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

    // ════════════════════════════════════════════════════════════════
    // Phase 2: RAG Ghost Text Commands
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Send a RAG (TM match) ghost text suggestion to the target editor.
    /// This appears as an inline completion with priority above burst.
    /// </summary>
    public async Task SetRagSuggestionAsync(string text, double score)
    {
        await PostCommandAsync("target", "setRagSuggestion", new
        {
            text,
            score = Math.Round(score, 3),
            providerId = "rdat-gtr",
            label = $"[GTR {score:P0}] TM Match"
        });

        _logger.LogDebug("[RDAT-Bridge] RAG suggestion sent: {Score:P0}", score);
    }

    /// <summary>
    /// Clear the RAG suggestion from the target editor.
    /// </summary>
    public async Task ClearRagSuggestionAsync()
    {
        await PostCommandAsync("target", "setRagSuggestion", new { text = "" });
    }

    /// <summary>
    /// Highlight a specific line in the source editor.
    /// Used to show which source sentence a TM match came from.
    /// </summary>
    public async Task HighlightSourceLineAsync(int lineNumber)
    {
        await PostCommandAsync("source", "highlightLine", new { lineNumber });
    }

    // ════════════════════════════════════════════════════════════════
    // Phase 4: Grammar Checker Commands
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Send grammar error markers to the target editor.
    /// Each marker appears as a squiggly underline with a hover tooltip.
    /// </summary>
    public async Task SetGrammarMarkersAsync(object[] markers)
    {
        await PostCommandAsync("target", "setGrammarMarkers", new { markers });
        _logger.LogDebug("[RDAT-Bridge] Grammar markers sent: {Count}", markers.Length);
    }

    /// <summary>
    /// Clear all grammar markers from the target editor.
    /// </summary>
    public async Task ClearGrammarMarkersAsync()
    {
        await PostCommandAsync("target", "setGrammarMarkers", new { markers = Array.Empty<object>() });
        _logger.LogDebug("[RDAT-Bridge] Grammar markers cleared");
    }

    /// <summary>
    /// Apply a quick fix to the target editor.
    /// Replaces the text at the specified position with the corrected text.
    /// </summary>
    public async Task ApplyQuickFixAsync(int lineNumber, int startColumn, int endColumn, string newText)
    {
        await PostCommandAsync("target", "applyQuickFix", new
        {
            lineNumber,
            startColumn,
            endColumn,
            newText
        });
        _logger.LogInformation(
            "[RDAT-Bridge] Quick fix applied: L{Line} C{Start}-{End} → \"{Text}\"",
            lineNumber, startColumn, endColumn, newText.Length > 30 ? newText[..30] + "..." : newText);
    }

    // ════════════════════════════════════════════════════════════════
    // Phase 4: AMTA Lint Commands
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Send AMTA terminology lint markers to the target editor.
    /// These appear as info/warning decorations with term suggestions.
    /// </summary>
    public async Task SetAmtaLintMarkersAsync(object[] markers)
    {
        await PostCommandAsync("target", "setAmtaLintMarkers", new { markers });
        _logger.LogDebug("[RDAT-Bridge] AMTA lint markers sent: {Count}", markers.Length);
    }

    /// <summary>
    /// Clear all AMTA lint markers from the target editor.
    /// </summary>
    public async Task ClearAmtaLintMarkersAsync()
    {
        await PostCommandAsync("target", "setAmtaLintMarkers", new { markers = Array.Empty<object>() });
        _logger.LogDebug("[RDAT-Bridge] AMTA lint markers cleared");
    }
}

// ════════════════════════════════════════════════════════════════════
// Phase 2: Messenger Types for WebView → ViewModel communication
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// Message sent when the cursor position changes in the target editor.
/// </summary>
public record TargetCursorChangedMessage(int LineNumber, int Column);

/// <summary>
/// Message sent when the cursor position changes in the source editor.
/// </summary>
public record SourceCursorChangedMessage(int LineNumber, int Column);

/// <summary>
/// Message sent when the text content changes in the target editor.
/// </summary>
public record TargetTextChangedMessage(string Text);

/// <summary>
/// Message sent when the text content changes in the source editor.
/// </summary>
public record SourceTextChangedMessage(string Text);
