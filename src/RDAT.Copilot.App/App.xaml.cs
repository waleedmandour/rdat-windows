// ========================================================================
// RDAT Copilot - App.xaml.cs (Entry Point)
// Location: src/RDAT.Copilot.App/App.xaml.cs
// ========================================================================

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using RDAT.Copilot.App.Hosting;
using RDAT.Copilot.App.ViewModels;
using RDAT.Copilot.App.Views;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Services;
using RDAT.Copilot.Infrastructure.LanceDb;
using RDAT.Copilot.Infrastructure.Linting;
using RDAT.Copilot.Infrastructure.Monaco;
using RDAT.Copilot.Infrastructure.Onnx;

namespace RDAT.Copilot.App;

public partial class App : Application
{
    private static Window? _mainWindow;
    public static Window MainWindow => _mainWindow!;
    public static IServiceProvider Services { get; private set; } = null!;
    public static bool IsFirstLaunch { get; set; } = true;

    public App()
    {
        this.InitializeComponent();
        this.UnhandledException += App_UnhandledException;
        ConfigureServices();
    }

    private void ConfigureServices()
    {
        var services = new ServiceCollection();

        // Core
        services.AddSingleton<IAmtaLinterService, AmtaLinterService>();
        services.AddSingleton<ISemanticTmService, LanceDbTmService>();
        services.AddSingleton<ILlmInferenceService, OnnxLlmService>();
        services.AddSingleton<GhostTextCoordinator>();

        // Infrastructure
        services.AddSingleton<IEditorBridge, EditorBridge>();

        // App Services
        services.AddSingleton<StartupService>();

        // ViewModels
        services.AddTransient<TranslationViewModel>();

        Services = services.BuildServiceProvider();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mainWindow = new MainWindow();

        // Run startup sequence
        var startup = Services.GetRequiredService<StartupService>();
        await startup.InitializeAsync(_mainWindow);

        _mainWindow.Content = new ShellPage();
        _mainWindow.Title = "RDAT Copilot - Research Translation Assistant";
        _mainWindow.Activate();
    }

    private void App_UnhandledException(object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RDAT", "Logs");
        Directory.CreateDirectory(logDir);
        File.WriteAllText(
            Path.Combine(logDir, $"error-{DateTime.Now:yyyyMMdd-HHmmss}.log"),
            $"[{DateTime.Now:O}] {e.Exception}\n{e.Exception.StackTrace}");
    }
}
