using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Desktop.Services;
using RDAT.Copilot.Desktop.ViewModels;
using WinRT.Interop;

namespace RDAT.Copilot.Desktop.Views;

/// <summary>
/// Settings page for configuring language direction, API keys,
/// RAG pipeline (Phase 2), LLM model (Phase 3), and
/// Gemini Cloud AI + AMTA Glossary (Phase 4).
/// </summary>
public sealed partial class SettingsPage : Page
{
    private readonly ILogger<SettingsPage> _logger;
    private readonly SettingsViewModel _viewModel;
    private readonly IRagPipelineService _ragPipeline;
    private readonly ILocalInferenceService? _inferenceService;
    private readonly ILlmQueueService? _queueService;
    private readonly IGeminiCloudService? _geminiService;
    private readonly IAmtaLinterService? _amtaLinter;
    private readonly ICredentialService? _credentialService;

    public SettingsPage()
    {
        this.InitializeComponent();
        _logger = App.Services.GetRequiredService<ILogger<SettingsPage>>();
        _viewModel = App.Services.GetRequiredService<SettingsViewModel>();
        _ragPipeline = App.Services.GetRequiredService<IRagPipelineService>();
        _inferenceService = App.Services.GetService<ILocalInferenceService>();
        _queueService = App.Services.GetService<ILlmQueueService>();
        _geminiService = App.Services.GetService<IGeminiCloudService>();
        _amtaLinter = App.Services.GetService<IAmtaLinterService>();
        _credentialService = App.Services.GetService<ICredentialService>();

        this.DataContext = _viewModel;
        _logger.LogInformation("[RDAT] SettingsPage loaded");
    }

    /// <summary>
    /// Exposes the SettingsViewModel for XAML binding.
    /// </summary>
    public SettingsViewModel ViewModel => _viewModel;

    // ─── RAG Pipeline (Phase 2) ──────────────────────────────────────

    private async void InitRag_Click(object sender, RoutedEventArgs e)
    {
        var modelPath = _viewModel.EmbeddingModelPath;
        var dbPath = _viewModel.TmDbPath;

        if (string.IsNullOrWhiteSpace(modelPath) || string.IsNullOrWhiteSpace(dbPath))
        {
            _viewModel.SaveStatus = "Please specify both model and database paths.";
            return;
        }

        _viewModel.IsSaving = true;
        _viewModel.SaveStatus = "Initializing RAG pipeline...";

        try
        {
            var progress = new Progress<(double Progress, string Text)>(p =>
            {
                _viewModel.SaveStatus = p.Text;
            });

            await _ragPipeline.InitializeAsync(modelPath, dbPath, progress);
            _viewModel.SaveStatus = $"RAG pipeline ready! {_ragPipeline.TotalTmCount:N0} TM entries loaded.";
            UpdateRagStatus();
        }
        catch (Exception ex)
        {
            _viewModel.SaveStatus = $"Failed: {ex.Message}";
            _logger.LogError(ex, "[RDAT] RAG initialization failed");
        }
        finally
        {
            _viewModel.IsSaving = false;
        }
    }

    private async void BrowseModel_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        var hwnd = WindowNative.GetWindowHandle(App.Services.GetRequiredService<MainWindow>());
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) _viewModel.EmbeddingModelPath = folder.Path;
    }

    private async void BrowseDb_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        var hwnd = WindowNative.GetWindowHandle(App.Services.GetRequiredService<MainWindow>());
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) _viewModel.TmDbPath = folder.Path;
    }

    private void UpdateRagStatus()
    {
        _viewModel.RagPipelineState = _ragPipeline.State.ToString();
        _viewModel.TmEntryCount = _ragPipeline.TotalTmCount.ToString("N0");
    }

    // ─── Phase 3: LLM Model ──────────────────────────────────────────

    private async void LoadLlm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_viewModel.LlmModelPath))
        {
            _viewModel.SaveStatus = "Please specify the LLM model path.";
            return;
        }

        if (_inferenceService is null)
        {
            _viewModel.SaveStatus = "LLM service not available.";
            return;
        }

        _viewModel.IsLlmInitializing = true;
        _viewModel.SaveStatus = "Loading LLM model...";

        try
        {
            var progress = new Progress<(double Progress, string Text)>(p =>
            {
                _viewModel.SaveStatus = p.Text;
                _viewModel.LlmEngineState = p.Text;
            });

            await _inferenceService.InitializeAsync(_viewModel.LlmModelPath, progress);
            _viewModel.SaveStatus = $"LLM model loaded successfully!";
            _viewModel.UpdateLlmState();

            // Start the queue
            if (_queueService is not null)
            {
                await _queueService.StartAsync();
                _viewModel.UpdateLlmState();
            }

            _logger.LogInformation("[RDAT] LLM model loaded: {Path}", _viewModel.LlmModelPath);
        }
        catch (Exception ex)
        {
            _viewModel.SaveStatus = $"LLM load failed: {ex.Message}";
            _viewModel.LlmEngineState = "Error ✗";
            _logger.LogError(ex, "[RDAT] LLM initialization failed");
        }
        finally
        {
            _viewModel.IsLlmInitializing = false;
        }
    }

    private async void UnloadLlm_Click(object sender, RoutedEventArgs e)
    {
        if (_inferenceService is null) return;

        try
        {
            // Stop queue first
            if (_queueService is not null)
            {
                await _queueService.StopAsync();
            }

            await _inferenceService.UnloadAsync();
            _viewModel.LlmEngineState = "Not Loaded";
            _viewModel.SaveStatus = "LLM model unloaded.";
            _viewModel.UpdateLlmState();
            _logger.LogInformation("[RDAT] LLM model unloaded");
        }
        catch (Exception ex)
        {
            _viewModel.SaveStatus = $"Unload failed: {ex.Message}";
            _logger.LogError(ex, "[RDAT] LLM unload failed");
        }
    }

    private async void BrowseLlmModel_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        var hwnd = WindowNative.GetWindowHandle(App.Services.GetRequiredService<MainWindow>());
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) _viewModel.LlmModelPath = folder.Path;
    }

    // ─── Phase 4: Gemini Cloud AI ────────────────────────────────────

    private async void SaveGeminiKey_Click(object sender, RoutedEventArgs e)
    {
        var key = GeminiApiKeyBox.Password;
        if (string.IsNullOrWhiteSpace(key))
        {
            _viewModel.SaveStatus = "Please enter a valid API key.";
            return;
        }

        _viewModel.GeminiApiKey = key;
        await _viewModel.SaveGeminiApiKeyCommand.ExecuteAsync(null);
    }

    private async void ValidateGeminiKey_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.ValidateGeminiKeyCommand.ExecuteAsync(null);
    }

    private async void RemoveGeminiKey_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.RemoveGeminiApiKeyCommand.ExecuteAsync(null);
        GeminiApiKeyBox.Password = string.Empty;
    }

    // ─── Phase 4: AMTA Glossary ───────────────────────────────────────

    private async void BrowseGlossary_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        var hwnd = WindowNative.GetWindowHandle(App.Services.GetRequiredService<MainWindow>());
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".csv");
        picker.FileTypeFilter.Add(".json");
        picker.FileTypeFilter.Add(".tsv");
        picker.FileTypeFilter.Add(".txt");
        var file = await picker.PickSingleFileAsync();
        if (file is not null) _viewModel.GlossaryPath = file.Path;
    }

    private async void LoadGlossary_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_viewModel.GlossaryPath))
        {
            _viewModel.SaveStatus = "Please specify a glossary file path.";
            return;
        }

        if (_amtaLinter is null)
        {
            _viewModel.SaveStatus = "AMTA linter service not available.";
            return;
        }

        _viewModel.IsGlossaryLoading = true;
        _viewModel.SaveStatus = "Loading glossary...";

        try
        {
            var progress = new Progress<(double Progress, string Text)>(p =>
            {
                _viewModel.SaveStatus = p.Text;
            });

            var workspaceVm = App.Services.GetRequiredService<WorkspaceViewModel>();
            await workspaceVm.LoadGlossaryAsync(_viewModel.GlossaryPath, progress);
            _viewModel.UpdateAmtaState();
            _viewModel.SaveStatus = $"Glossary loaded: {_amtaLinter.TermCount} terms.";
            _logger.LogInformation("[RDAT] Glossary loaded: {Count} terms", _amtaLinter.TermCount);
        }
        catch (Exception ex)
        {
            _viewModel.SaveStatus = $"Glossary load failed: {ex.Message}";
            _viewModel.UpdateAmtaState();
            _logger.LogError(ex, "[RDAT] Glossary load failed");
        }
        finally
        {
            _viewModel.IsGlossaryLoading = false;
        }
    }

    // ─── Navigation ─────────────────────────────────────────────────

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        var navigation = App.Services.GetRequiredService<NavigationService>();
        navigation.NavigateTo<WorkspacePage>();
    }
}
