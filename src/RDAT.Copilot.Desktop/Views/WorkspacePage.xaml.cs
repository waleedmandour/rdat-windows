using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using RDAT.Copilot.Desktop.Services;
using RDAT.Copilot.Desktop.ViewModels;
using WinRT.Interop;

namespace RDAT.Copilot.Desktop.Views;

/// <summary>
/// Main translation workspace page with split-pane layout.
/// Hosts two WebView2 controls for Monaco Editor (source and target).
/// Manages the C# <-> JavaScript interop bridge for ghost text,
/// grammar checking, and editor synchronization.
/// Phase 2: Added TM (Translation Memory) panel with RAG integration.
/// </summary>
public sealed partial class WorkspacePage : Page
{
    private readonly WebViewBridgeService _bridgeService;
    private readonly WorkspaceViewModel _viewModel;
    private readonly TmPanelViewModel _tmPanelViewModel;
    private readonly ILogger<WorkspacePage> _logger;

    /// <summary>
    /// Exposes the WorkspaceViewModel for XAML data binding.
    /// </summary>
    public WorkspaceViewModel ViewModel => _viewModel;

    /// <summary>
    /// Exposes the TmPanelViewModel for XAML data binding.
    /// </summary>
    public TmPanelViewModel TmPanelViewModel => _tmPanelViewModel;

    public WorkspacePage()
    {
        this.InitializeComponent();

        _bridgeService = App.Services.GetRequiredService<WebViewBridgeService>();
        _viewModel = App.Services.GetRequiredService<WorkspaceViewModel>();
        _tmPanelViewModel = App.Services.GetRequiredService<TmPanelViewModel>();
        _logger = App.Services.GetRequiredService<ILogger<WorkspacePage>>();

        this.DataContext = _viewModel;
        this.Loaded += WorkspacePage_Loaded;
        this.Unloaded += WorkspacePage_Unloaded;
    }

    private async void WorkspacePage_Loaded(object sender, RoutedEventArgs e)
    {
        _logger.LogInformation("[RDAT] WorkspacePage loaded — initializing WebView2 bridges");

        // Initialize Source WebView2 bridge
        await _bridgeService.InitializeAsync(SourceWebView, "source");

        // Initialize Target WebView2 bridge
        await _bridgeService.InitializeAsync(TargetWebView, "target");

        _logger.LogInformation("[RDAT] Both WebView2 bridges initialized successfully");

        // Register for messenger events from the bridge
        RegisterMessengerHandlers();

        // Update TM panel state from RAG pipeline
        _tmPanelViewModel.UpdateFromPipelineState();

        // Set initial source text in the Monaco editor
        await _bridgeService.SetTextAsync("source", _viewModel.SourceText);
    }

    private void WorkspacePage_Unloaded(object sender, RoutedEventArgs e)
    {
        // Unregister messenger handlers
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    /// <summary>
    /// Register messenger handlers for cursor/text events from the WebView2 bridge.
    /// Phase 2: Routes events through the ViewModel for RAG integration.
    /// </summary>
    private void RegisterMessengerHandlers()
    {
        // Target cursor changes → trigger RAG lookup
        WeakReferenceMessenger.Default.Register<TargetCursorChangedMessage>(this, async (r, msg) =>
        {
            _logger.LogDebug("[RDAT] Target cursor: L{Line}:C{Col}", msg.LineNumber, msg.Column);

            // Update ViewModel
            _viewModel.TargetEditor.CursorLine = msg.LineNumber;
            _viewModel.TargetEditor.CursorColumn = msg.Column;

            // Phase 2: Trigger RAG TM lookup for the corresponding source sentence
            await _viewModel.OnTargetCursorChangedAsync(msg.LineNumber, msg.Column);

            // Update TM panel best match
            var sourceSentence = _viewModel.SourceEditor.GetSourceSentence(msg.LineNumber);
            await _tmPanelViewModel.UpdateBestMatchAsync(sourceSentence);

            // If we have a RAG match, push it as ghost text to Monaco
            if (_viewModel.HasRagMatch && !string.IsNullOrEmpty(_viewModel.RagMatchText))
            {
                await _bridgeService.SetRagSuggestionAsync(
                    _viewModel.RagMatchText,
                    _viewModel.RagMatchScore);
            }
            else
            {
                await _bridgeService.ClearRagSuggestionAsync();
            }
        });

        // Source cursor changes
        WeakReferenceMessenger.Default.Register<SourceCursorChangedMessage>(this, (r, msg) =>
        {
            _viewModel.OnSourceCursorChanged(msg.LineNumber, msg.Column);
        });

        // Target text changes
        WeakReferenceMessenger.Default.Register<TargetTextChangedMessage>(this, (r, msg) =>
        {
            _viewModel.TargetText = msg.Text;
        });

        // Source text changes
        WeakReferenceMessenger.Default.Register<SourceTextChangedMessage>(this, (r, msg) =>
        {
            _viewModel.SourceEditor.Text = msg.Text;
            _viewModel.SourceText = msg.Text;
        });
    }

    // ─── TM Panel Event Handlers ────────────────────────────────────

    private void TmPanelTab_Click(object sender, RoutedEventArgs e)
    {
        _tmPanelViewModel.TogglePanel();
    }

    private void CloseTmPanel_Click(object sender, RoutedEventArgs e)
    {
        _tmPanelViewModel.IsPanelOpen = false;
    }

    private async void TmSearch_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        await _tmPanelViewModel.SearchTmAsync();
    }

    private async void TmSearch_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            await _tmPanelViewModel.SearchTmAsync();
        }
    }

    private async void ImportTm_Click(object sender, RoutedEventArgs e)
    {
        // Open file picker for TM import
        var picker = new Windows.Storage.Pickers.FileOpenPicker();

        // Get the window handle for the picker
        var hwnd = WindowNative.GetWindowHandle(
            App.Services.GetRequiredService<MainWindow>());
        InitializeWithWindow.Initialize(picker, hwnd);

        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationLocation.DocumentsLibrary;
        picker.FileTypeFilter.Add(".csv");
        picker.FileTypeFilter.Add(".tmx");
        picker.FileTypeFilter.Add(".tsv");
        picker.FileTypeFilter.Add(".txt");

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            _tmPanelViewModel.ImportFilePath = file.Path;
            await _tmPanelViewModel.ImportTmFileAsync();
        }
    }

    private async void RefreshTm_Click(object sender, RoutedEventArgs e)
    {
        await _tmPanelViewModel.RefreshStatsAsync();
    }

    // ─── Navigation ─────────────────────────────────────────────────

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
