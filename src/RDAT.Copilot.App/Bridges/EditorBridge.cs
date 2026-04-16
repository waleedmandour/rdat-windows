using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Models;
using RDAT.Copilot.Core.Services;

namespace RDAT.Copilot.App.Bridges;

/// <summary>
/// Bidirectional bridge between WinUI 3 WebView2 (Monaco Editor) and
/// the C# GhostTextCoordinator. Implements IEditorBridge for DI injection.
///
/// Uses CoreWebView2 (from Microsoft.Web.WebView2 SDK) for messaging
/// to avoid WinUI event args type resolution issues across package versions.
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
    public void Attach(object webView)
    {
        if (webView is not WebView2 wv)
            throw new ArgumentException($"Expected WebView2, got {webView?.GetType().Name ?? "null"}", nameof(webView));

        _webView = wv;

        // Initialize CoreWebView2 and subscribe to its messaging events
        _ = InitializeCoreWebView2Async();
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

    private async Task InitializeCoreWebView2Async()
    {
        try
        {
            if (_webView == null) return;
            await _webView.EnsureCoreWebView2Async();

            if (_webView.CoreWebView2 != null)
            {
                _webView.CoreWebView2.WebMessageReceived += OnCoreWebMessageReceived;
                _logger.LogInformation("EditorBridge attached to CoreWebView2");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize CoreWebView2 for EditorBridge");
        }
    }

    /// <summary>
    /// Handles incoming messages from the Monaco JavaScript layer via CoreWebView2.
    /// </summary>
    private void OnCoreWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
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

        _coordinator.OnKeystroke(keystroke);
    }

    private void OnGhostTextReceived(GhostTextResult result)
    {
        PostGhostTextResult(result);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_webView?.CoreWebView2 != null)
        {
            _webView.CoreWebView2.WebMessageReceived -= OnCoreWebMessageReceived;
        }

        _ghostTextSubscription?.Dispose();
    }
}
