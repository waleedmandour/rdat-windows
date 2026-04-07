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
    /// Registers Core services (embedding, vector DB, RAG pipeline, LLM, ghost text,
    /// grammar checker, AMTA linter, Gemini cloud) and Desktop ViewModels in the DI container.
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

        // ─── Core Services (Phase 4: Grammar, AMTA, Gemini) ─────────
        services.AddSingleton<IGrammarCheckerService, GrammarCheckerService>();
        services.AddSingleton<IAmtaLinterService, AmtaLinterService>();
        services.AddSingleton<ICredentialService, CredentialLockerService>();
        // Register named HttpClient for Gemini API (managed lifecycle, prevents socket exhaustion)
        services.AddHttpClient("Gemini", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "RDAT-Copilot/2.0");
        });
        services.AddSingleton<IGeminiCloudService, GeminiCloudService>();

        // ─── Core Services (Phase 5: Document Import) ──────────────
        services.AddSingleton<IDocxImportService, DocxImportService>();

        // ─── Desktop Services ───────────────────────────────────────
        services.AddSingleton<IWebViewBridge, WebViewBridgeService>();
        services.AddTransient<NavigationService>();

        // ─── ViewModels ─────────────────────────────────────────────
        services.AddSingleton<WorkspaceViewModel>();
        services.AddSingleton<SourceEditorViewModel>();
        services.AddSingleton<TargetEditorViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<TmPanelViewModel>();

        return services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mainWindow = new MainWindow();
        _mainWindow.Activate();
        _mainWindow.Closed += async (s, e) =>
        {
            // Gracefully shutdown services
            try
            {
                var queue = Services.GetService<ILlmQueueService>();
                if (queue is not null) await queue.StopAsync();

                var coordinator = Services.GetService<IGhostTextCoordinator>();
                if (coordinator is not null) await coordinator.StopAsync();

                var rag = Services.GetService<IRagPipelineService>();
                if (rag is not null) await rag.ShutdownAsync();

                // Dispose the DI container to release all IDisposable services
                if (Services is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch (Exception ex)
            {
                Services.GetRequiredService<ILogger<App>>()
                    .LogDebug(ex, "[RDAT] Error during service shutdown");
            }
        };
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Log unhandled exceptions
        var logger = Services.GetRequiredService<ILogger<App>>();
        logger.LogError(e.Exception, "Unhandled exception: {Message}", e.Exception.Message);

        // Prevent the exception from crashing the app during development
        e.Handled = true;
    }
}
