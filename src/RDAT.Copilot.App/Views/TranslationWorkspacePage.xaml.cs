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

    private void TranslationWorkspacePage_Loaded(object sender, RoutedEventArgs e)
    {
        // Load the Monaco editor files programmatically after the UI is ready
        try {
            SourceWebView.Source = new Uri("ms-appx:///Assets/monaco-editor-host.html");
            TargetWebView.Source = new Uri("ms-appx:///Assets/monaco-editor-host.html");
        } catch { }
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
        _ = TargetWebView.ExecuteScriptAsync($"window.setTextDirection && window.setTextDirection('{direction}')");
    }
}