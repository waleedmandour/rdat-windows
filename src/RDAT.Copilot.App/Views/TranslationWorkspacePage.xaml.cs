using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace RDAT.Copilot.App.Views;

public sealed partial class TranslationWorkspacePage : Page
{
    private int _currentSegment = 1;
    private int _totalSegments = 1;
    private bool _isRtl = true;

    public TranslationWorkspacePage()
    {
        this.InitializeComponent();
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Implement file picker for .docx files
        StatusText.Text = "Open file dialog not yet connected.";
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Implement save functionality
        StatusText.Text = "Save not yet connected.";
    }

    private void PrevSegmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSegment > 1)
        {
            _currentSegment--;
            UpdateSegmentIndicator();
            StatusText.Text = $"Navigated to segment {_currentSegment}.";
        }
    }

    private void NextSegmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSegment < _totalSegments)
        {
            _currentSegment++;
            UpdateSegmentIndicator();
            StatusText.Text = $"Navigated to segment {_currentSegment}.";
        }
    }

    private void RtlToggle_Click(object sender, RoutedEventArgs e)
    {
        _isRtl = RtlToggle.IsChecked == true;
        RtlStatusText.Text = _isRtl ? "RTL" : "LTR";
        StatusText.Text = $"Target editor direction set to {(_isRtl ? "RTL" : "LTR")}.";

        // Notify WebView2 to change text direction
        var direction = _isRtl ? "rtl" : "ltr";
        _ = TargetWebView.ExecuteScriptAsync($"window.setTextDirection && window.setTextDirection('{direction}')");
    }

    private void UpdateSegmentIndicator()
    {
        SegmentIndicator.Text = $"Segment {_currentSegment} / {_totalSegments}";
    }
}
