using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using RDAT.Copilot.Core.Orchestration;

namespace RDAT.Copilot.Infrastructure.Monaco;

/// <summary>
/// Bridges Monaco Editor keystrokes from WebView2 JavaScript to the C# GhostTextCoordinator.
/// </summary>
public sealed class EditorBridge : IDisposable
{
    private readonly GhostTextCoordinator _coordinator;
    private readonly ILogger<EditorBridge> _logger;
    private CoreWebView2? _webview;
    private IDisposable? _coordinatorSubscription;

    // Subject to stream incoming keystroke events to the coordinator
    private readonly Subject<(string Source, string Target, string Lang)> _keystrokeSubject = new();

    public EditorBridge(GhostTextCoordinator coordinator, ILogger<EditorBridge> logger)
    {
        _coordinator = coordinator;
        _logger = logger;
        
        // Connect the bridge to the coordinator
        _coordinator.ConnectInput(_keystrokeSubject.AsObservable());

        // Listen for AI responses and send them to the JS layer
        _coordinatorSubscription = _coordinator.GhostTextStream
            .ObserveOn(SynchronizationContext.Current ?? new SynchronizationContext()) // Post to UI thread
            .Subscribe(result =>
            {
                if (_webview == null) return;

                var payload = new
                {
                    type = "ghosTextResult",
                    text = result?.Text ?? "" // Empty string clears decoration
                };

                _webview.PostWebMessageAsJson(JsonSerializer.Serialize(payload));
            });
    }

    /// <summary>
    /// Attach the active WebView2 instance to handle messages.
    /// </summary>
    public void Attach(CoreWebView2 webview)
    {
        _webview = webview;
        _webview.WebMessageReceived += OnWebMessageReceived;
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.WebMessageAsJson;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "keystroke")
            {
                string sourceText = root.GetProperty("source").GetString() ?? "";
                string targetText = root.GetProperty("target").GetString() ?? "";
                string langPair = root.GetProperty("lang").GetString() ?? "en-es";

                // Push over reactive bridge
                _keystrokeSubject.OnNext((sourceText, targetText, langPair));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse WebView message.");
        }
    }

    public void Dispose()
    {
        if (_webview != null)
        {
            _webview.WebMessageReceived -= OnWebMessageReceived;
        }
        _coordinatorSubscription?.Dispose();
        _keystrokeSubject.OnCompleted();
        _keystrokeSubject.Dispose();
        _coordinator.Dispose();
    }
}
