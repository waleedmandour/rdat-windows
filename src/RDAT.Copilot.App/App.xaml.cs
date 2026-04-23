using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.IO;
// This alias is critical: it prevents the compiler from confusing
// System.IO.Path with Microsoft.UI.Xaml.Shapes.Path
using Path = System.IO.Path;

using RDAT.Copilot.App.Bridges;
using RDAT.Copilot.App.ViewModels;
using RDAT.Copilot.App.Views;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Services;
using RDAT.Copilot.Infrastructure.Gemini;
using RDAT.Copilot.Infrastructure.LanceDb;
using RDAT.Copilot.Infrastructure.Linting;
using RDAT.Copilot.Infrastructure.Onnx;

namespace RDAT.Copilot.App;

public partial class App : Application
{
    private static Window? _mainWindow;

    /// <summary>
    /// Static access to the main window.
    /// </summary>
    public static Window MainWindow => _mainWindow!;

    /// <summary>
    /// Centralized Service Provider for Dependency Injection.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        // InitializeComponent must be called first for XAML loading
        this.InitializeComponent();

        this.UnhandledException += App_UnhandledException;
        ConfigureServices();
    }

    private void ConfigureServices()
    {
        var services = new ServiceCollection();

        // 1. Core AI Services (Backend)
        services.AddSingleton<IAmtaLinterService, AmtaLinterService>();
        services.AddSingleton<ISemanticTmService, LanceDbTmService>();
        services.AddSingleton<ILlmInferenceService, OnnxLlmService>();
        services.AddSingleton<IGeminiService, GeminiCloudService>();
        services.AddSingleton<GhostTextCoordinator>();

        // 2. Editor Bridge (WebView2 <-> Monaco)
        services.AddSingleton<IEditorBridge, EditorBridge>();

        // 3. ViewModels (Frontend Logic)
        services.AddTransient<TranslationViewModel>();

        Services = services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mainWindow = new MainWindow();
        _mainWindow.Content = new ShellPage();
        _mainWindow.Activate();
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Prevent the app from crashing immediately, try to log the error
        e.Handled = true;
        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RDAT", "Logs");
            if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);

            var logPath = Path.Combine(logDir, $"error-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(logPath, e.Exception.ToString() + "\n" + e.Message);
        }
        catch
        {
            // If logging fails, there's nothing more we can do
        }
    }
}
