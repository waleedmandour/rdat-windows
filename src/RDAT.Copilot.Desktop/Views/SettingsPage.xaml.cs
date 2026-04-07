using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RDAT.Copilot.Desktop.Services;

namespace RDAT.Copilot.Desktop.Views;

/// <summary>
/// Settings page for configuring language direction,
/// API keys (stored in Windows Credential Locker), and AI model preferences.
/// </summary>
public sealed partial class SettingsPage : Page
{
    private readonly ILogger<SettingsPage> _logger;

    public SettingsPage()
    {
        this.InitializeComponent();
        _logger = App.Services.GetRequiredService<ILogger<SettingsPage>>();
        _logger.LogInformation("[RDAT] SettingsPage loaded");
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        var navigation = App.Services.GetRequiredService<NavigationService>();
        navigation.NavigateTo<WorkspacePage>();
    }
}
