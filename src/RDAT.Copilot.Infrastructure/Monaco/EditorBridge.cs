using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Models;
using RDAT.Copilot.Core.Services;

namespace RDAT.Copilot.Infrastructure.Monaco;

/// <summary>
/// Bidirectional bridge between WinUI 3 WebView2 (Monaco Editor) and
/// the C# GhostTextCoordinator. Implements IEditorBridge for DI injection.
///
/// Flow: Monaco JS → WebView2 → EditorBridge → GhostTextCoordinator → LLM → Linter → EditorBridge → WebView2 → Monaco JS
/// </summary>
public sealed class EditorBridge : IEditorBridge
{
    private readonly GhostTextCoordinator _coordinator;
    private readonly ILogger<EditorBridge> _logger;
    private WebView2? _webView;
    private IDisposable? _ghostTextSubscription;
    private bool _isDisposed;

    public EditorBridge(GhostTextCoordinator coordinator, ILogger<EditorBridge> logger)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to coordinator's ghost text stream and push results to Monaco
        _ghostTextSubscription = _coordinator.GhostTextStream
            .ObserveOn(SynchronizationContext.Current ?? new SynchronizationContext())
            .Subscribe(OnGhostTextReceived, ex =>
            {
                _logger.LogError(ex, "Error in ghost text stream subscription");
            });
    }

    /// <summary>
    /// Observable stream of keystroke events forwarded from the Monaco editor.
    /// </summary>
    public IObservable<EditorKeystroke> KeystrokeStream => _coordinator.KeystrokeStream;

    /// <summary>
    /// Observable stream of ghost text prediction results from the coordinator.
    /// </summary>
    public IObservable<GhostTextResult> GhostTextStream => _coordinator.GhostTextStream;

    /// <summary>
    /// Attaches the bridge to a WinUI 3 WebView2 control and begins listening
    /// for keystroke messages from the Monaco JavaScript layer.
    /// </summary>
    public void Attach(WebView2 webView)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _webView.WebMessageReceived += OnWebMessageReceived;
        _logger.LogInformation("EditorBridge attached to WebView2");
    }

    /// <summary>
    /// Posts a ghost text prediction result to the Monaco editor via WebView2.
    /// </summary>
    public void PostGhostTextResult(GhostTextResult result)
    {
        if (_webView?.CoreWebView2 == null) return;

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                type = "ghosTextResult",
                text = result?.Text ?? ""
            });
            _webView.CoreWebView2.PostWebMessageAsJson(payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post ghost text result to WebView2");
        }
    }

    /// <summary>
    /// Sets the editor text directionality (RTL or LTR).
    /// Posts a direction change message to the Monaco JavaScript layer.
    /// </summary>
    public void SetDirection(bool isRtl)
    {
        if (_webView?.CoreWebView2 == null) return;

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                type = "setDirection",
                isRtl
            });
            _webView.CoreWebView2.PostWebMessageAsJson(payload);
            _logger.LogDebug("Editor direction set to {Direction}", isRtl ? "RTL" : "LTR");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set editor direction");
        }
    }

    /// <summary>
    /// Clears the current ghost text decoration from the Monaco editor.
    /// </summary>
    public void ClearGhostText()
    {
        PostGhostTextResult(new GhostTextResult { Text = "" });
    }

    /// <summary>
    /// Handles incoming messages from the Monaco JavaScript layer.
    /// Parses keystroke events and forwards them to the GhostTextCoordinator.
    /// </summary>
    private void OnWebMessageReceived(WebView2 sender, WebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string json = e.WebMessageAsJson;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp)) return;
            string messageType = typeProp.GetString() ?? "";

            switch (messageType)
            {
                case "keystroke":
                    HandleKeystrokeMessage(root);
                    break;
                case "ghostTextAccepted":
                    _logger.LogDebug("Ghost text accepted by user");
                    break;
                case "ghostTextRejected":
                    ClearGhostText();
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse WebView2 message");
        }
    }

    private void HandleKeystrokeMessage(JsonElement root)
    {
        string sourceText = root.TryGetProperty("source", out var src) ? src.GetString() ?? "" : "";
        string targetText = root.TryGetProperty("target", out var tgt) ? tgt.GetString() ?? "" : "";
        string lang = root.TryGetProperty("lang", out var lng) ? lng.GetString() ?? "en-ar" : "en-ar";

        bool isRtl = lang.StartsWith("en", StringComparison.OrdinalIgnoreCase);

        var keystroke = new EditorKeystroke
        {
            SourceText = sourceText,
            TargetText = targetText,
            Language = isRtl ? "ar" : "en",
            IsRtl = isRtl
        };

        // Forward to coordinator which handles debouncing and prediction pipeline
        _coordinator.OnKeystroke(keystroke);
    }

    /// <summary>
    /// Callback when the coordinator produces a ghost text prediction.
    /// Pushes the result to the Monaco editor via WebView2.
    /// </summary>
    private void OnGhostTextReceived(GhostTextResult result)
    {
        PostGhostTextResult(result);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_webView != null)
        {
            _webView.WebMessageReceived -= OnWebMessageReceived;
        }

        _ghostTextSubscription?.Dispose();
        // Note: Do NOT dispose the coordinator here - it's owned by the DI container
    }
}
