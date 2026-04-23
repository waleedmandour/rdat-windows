using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace RDAT.Copilot.App.Views;

public sealed partial class TranslationWorkspacePage : Page
{
    public TranslationWorkspacePage()
    {
        this.InitializeComponent();
        this.Loaded += TranslationWorkspacePage_Loaded;
    }

    private async void TranslationWorkspacePage_Loaded(object sender, RoutedEventArgs e)
    {
        // Load the Monaco editor HTML into WebView2 controls
        // For unpackaged WinUI 3 apps, ms-appx:// URIs work through the
        // Windows App SDK's URI handler, but we fall back to file:// if needed.
        try
        {
            // Initialize WebView2 first
            await SourceWebView.EnsureCoreWebView2Async();
            await TargetWebView.EnsureCoreWebView2Async();

            // Load the Monaco editor host page from app assets
            // ms-appx-web:/// is the correct scheme for WebView2 content in WinUI 3
            SourceWebView.Source = new Uri("ms-appx-web:///Assets/monaco-editor-host.html");
            TargetWebView.Source = new Uri("ms-appx-web:///Assets/monaco-editor-host.html");

            StatusText.Text = "Editor loaded. Ready for translation.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Editor load error: {ex.Message}";

            // Fallback: try file-based loading from the app directory
            try
            {
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var htmlPath = System.IO.Path.Combine(appDir, "Assets", "monaco-editor-host.html");

                if (System.IO.File.Exists(htmlPath))
                {
                    SourceWebView.Source = new Uri($"file:///{htmlPath.Replace('\\', '/')}");
                    TargetWebView.Source = new Uri($"file:///{htmlPath.Replace('\\', '/')}");
                    StatusText.Text = "Editor loaded (local file fallback).";
                }
                else
                {
                    // Last resort: embed a minimal editor directly
                    LoadMinimalEditor();
                }
            }
            catch
            {
                LoadMinimalEditor();
            }
        }
    }

    /// <summary>
    /// Loads a minimal inline HTML editor as a last resort when Monaco files
    /// are not available locally or via CDN.
    /// </summary>
    private void LoadMinimalEditor()
    {
        var minimalHtml = @"<!DOCTYPE html>
<html><head><meta charset=""UTF-8""><style>
body { margin:0; padding:8px; background:#1e1e1e; color:#d4d4d4; font-family:'Segoe UI',sans-serif; }
textarea { width:100%; height:100%; background:#1e1e1e; color:#d4d4d4; border:1px solid #333;
  font-size:14px; padding:8px; resize:none; outline:none; }
textarea:focus { border-color:#6495ED; }
</style></head><body>
<textarea placeholder=""Type translation here..."" spellcheck=""false""></textarea>
</body></html>";

        SourceWebView.NavigateToString(minimalHtml);
        TargetWebView.NavigateToString(minimalHtml);
        StatusText.Text = "Minimal editor loaded. Download Monaco for full features.";
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Opening file...";
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e) { }
    private void PrevSegmentButton_Click(object sender, RoutedEventArgs e) { }
    private void NextSegmentButton_Click(object sender, RoutedEventArgs e) { }

    private void RtlToggle_Click(object sender, RoutedEventArgs e)
    {
        var direction = (RtlToggle.IsChecked == true) ? "rtl" : "ltr";
        _ = TargetWebView.ExecuteScriptAsync(
            $"(function(){{ window.rdatSetDirection && window.rdatSetDirection({(direction == "rtl" ? "true" : "false")}); }})()");
    }
}
