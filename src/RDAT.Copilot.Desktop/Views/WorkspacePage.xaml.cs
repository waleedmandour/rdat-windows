using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RDAT.Copilot.Desktop.Services;
using RDAT.Copilot.Desktop.ViewModels;

namespace RDAT.Copilot.Desktop.Views;

/// <summary>
/// Main translation workspace page with split-pane layout.
/// Hosts two WebView2 controls for Monaco Editor (source and target).
/// Manages the C# <-> JavaScript interop bridge for ghost text,
/// grammar checking, and editor synchronization.
/// </summary>
public sealed partial class WorkspacePage : Page
{
    private readonly WebViewBridgeService _bridgeService;
    private readonly WorkspaceViewModel _viewModel;
    private readonly ILogger<WorkspacePage> _logger;

    public WorkspacePage()
    {
        this.InitializeComponent();

        _bridgeService = App.Services.GetRequiredService<WebViewBridgeService>();
        _viewModel = App.Services.GetRequiredService<WorkspaceViewModel>();
        _logger = App.Services.GetRequiredService<ILogger<WorkspacePage>>();

        this.DataContext = _viewModel;
        this.Loaded += WorkspacePage_Loaded;
    }

    private async void WorkspacePage_Loaded(object sender, RoutedEventArgs e)
    {
        _logger.LogInformation("[RDAT] WorkspacePage loaded — initializing WebView2 bridges");

        // Initialize Source WebView2 bridge
        await _bridgeService.InitializeAsync(SourceWebView, "source");

        // Initialize Target WebView2 bridge
        await _bridgeService.InitializeAsync(TargetWebView, "target");

        _logger.LogInformation("[RDAT] Both WebView2 bridges initialized successfully");
    }

    private void EditSource_Click(object sender, RoutedEventArgs e)
    {
        _logger.LogInformation("[RDAT] Source edit button clicked (Phase 5: native overlay)");
    }

    private void SettingsTab_Click(object sender, RoutedEventArgs e)
    {
        var navigation = App.Services.GetRequiredService<NavigationService>();
        navigation.NavigateTo<SettingsPage>();
    }
}
