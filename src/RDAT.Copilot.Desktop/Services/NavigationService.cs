using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace RDAT.Copilot.Desktop.Services;

/// <summary>
/// Simple frame-based navigation service for the WinUI 3 application.
/// Manages page transitions and back-navigation.
/// </summary>
public class NavigationService
{
    private readonly ILogger<NavigationService> _logger;
    private Frame? _frame;

    public Frame? Frame
    {
        get => _frame;
        set
        {
            _frame = value;
            if (_frame is not null)
            {
                _frame.Navigated += OnNavigated;
            }
        }
    }

    public NavigationService(ILogger<NavigationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Navigates to the specified page type.
    /// </summary>
    public void NavigateTo<TPage>() where TPage : Page
    {
        if (_frame is null)
        {
            _logger.LogWarning("[RDAT-Nav] Frame is not set — cannot navigate");
            return;
        }

        _logger.LogInformation("[RDAT-Nav] Navigating to {Page}", typeof(TPage).Name);
        _frame.Navigate(typeof(TPage));
    }

    /// <summary>
    /// Navigates back to the previous page.
    /// </summary>
    public void GoBack()
    {
        if (_frame?.CanGoBack == true)
        {
            _frame.GoBack();
            _logger.LogInformation("[RDAT-Nav] Navigated back");
        }
    }

    private void OnNavigated(object sender, NavigationEventArgs e)
    {
        _logger.LogDebug("[RDAT-Nav] Navigated to {Page}", e.SourcePageType.Name);
    }
}
