using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Desktop.Services;
using RDAT.Copilot.Desktop.ViewModels;

namespace RDAT.Copilot.Desktop.Views;

/// <summary>
/// Settings page for configuring language direction, API keys,
/// RAG pipeline (Phase 2), and AI model preferences.
/// Phase 2: Added Translation Memory database and embedding model configuration.
/// </summary>
public sealed partial class SettingsPage : Page
{
    private readonly ILogger<SettingsPage> _logger;
    private readonly SettingsViewModel _viewModel;
    private readonly IRagPipelineService _ragPipeline;

    public SettingsPage()
    {
        this.InitializeComponent();
        _logger = App.Services.GetRequiredService<ILogger<SettingsPage>>();
        _viewModel = App.Services.GetRequiredService<SettingsViewModel>();
        _ragPipeline = App.Services.GetRequiredService<IRagPipelineService>();

        this.DataContext = _viewModel;
        _logger.LogInformation("[RDAT] SettingsPage loaded");
    }

    /// <summary>
    /// Exposes the SettingsViewModel for XAML binding.
    /// </summary>
    public SettingsViewModel ViewModel => _viewModel;

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

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(
            App.Services.GetRequiredService<MainWindow>());
        InitializeWithWindow.Initialize(picker, hwnd);

        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            _viewModel.EmbeddingModelPath = folder.Path;
        }
    }

    private async void BrowseDb_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(
            App.Services.GetRequiredService<MainWindow>());
        InitializeWithWindow.Initialize(picker, hwnd);

        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            _viewModel.TmDbPath = folder.Path;
        }
    }

    private void UpdateRagStatus()
    {
        _viewModel.RagPipelineState = _ragPipeline.State.ToString();
        _viewModel.TmEntryCount = _ragPipeline.TotalTmCount.ToString("N0");
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        var navigation = App.Services.GetRequiredService<NavigationService>();
        navigation.NavigateTo<WorkspacePage>();
    }
}
