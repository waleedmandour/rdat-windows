using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using RDAT.Copilot.Desktop.Services;
using RDAT.Copilot.Desktop.ViewModels;

namespace RDAT.Copilot.Desktop;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// Entry point for DI container setup and WinUI 3 initialization.
/// </summary>
public partial class App : Application
{
    private Window? _mainWindow;

    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        this.InitializeComponent();

        // Configure Dependency Injection
        Services = ConfigureServices();

        // Handle unhandled exceptions
        this.UnhandledException += App_UnhandledException;
    }

    /// <summary>
    /// Configures all services and ViewModels for dependency injection.
    /// </summary>
    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddDebug();
        });

        // Services
        services.AddSingleton<WebViewBridgeService>();
        services.AddSingleton<NavigationService>();

        // ViewModels
        services.AddTransient<WorkspaceViewModel>();
        services.AddTransient<SourceEditorViewModel>();
        services.AddTransient<TargetEditorViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mainWindow = new MainWindow();
        _mainWindow.Activate();
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Log unhandled exceptions
        var logger = Services.GetRequiredService<ILogger<App>>();
        logger.LogError(e.Exception, "Unhandled exception: {Message}", e.Exception.Message);

        // Prevent the exception from crashing the app during development
#if DEBUG
        e.Handled = true;
#else
        e.Handled = true;
#endif
    }
}
