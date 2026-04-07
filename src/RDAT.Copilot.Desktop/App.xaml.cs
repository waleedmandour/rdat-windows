using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Services;
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
    /// Registers Core services (embedding, vector DB, RAG pipeline, LLM, ghost text)
    /// and Desktop ViewModels in the DI container.
    /// </summary>
    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddDebug();
        });

        // ─── Core Services (Phase 2: RAG Pipeline) ──────────────────
        services.AddSingleton<IEmbeddingService, OnnxEmbeddingService>();
        services.AddSingleton<IVectorDatabaseService, LanceVectorDbService>();
        services.AddSingleton<ITmImportService, TmImportService>();
        services.AddSingleton<IRagPipelineService, RagPipelineService>();

        // ─── Core Services (Phase 3: LLM Queue Engine) ──────────────
        services.AddSingleton<ILocalInferenceService, OnnxLlmInferenceService>();
        services.AddSingleton<ILlmQueueService, LlmQueueService>();
        services.AddSingleton<IGhostTextCoordinator, GhostTextCoordinator>();

        // ─── Desktop Services ───────────────────────────────────────
        services.AddSingleton<WebViewBridgeService>();
        services.AddSingleton<NavigationService>();

        // ─── ViewModels ─────────────────────────────────────────────
        services.AddTransient<WorkspaceViewModel>();
        services.AddTransient<SourceEditorViewModel>();
        services.AddTransient<TargetEditorViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<TmPanelViewModel>();

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
