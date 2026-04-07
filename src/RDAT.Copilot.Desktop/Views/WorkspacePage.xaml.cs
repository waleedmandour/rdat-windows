using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Models;
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
/// Phase 3: Integrated LLM queue engine for Burst/Pause/Prefetch ghost text.
/// Phase 4: Integrated grammar markers, AMTA lint panel, quality estimation.
/// </summary>
public sealed partial class WorkspacePage : Page
{
    private readonly IWebViewBridge _bridgeService;
    private readonly WorkspaceViewModel _viewModel;
    private readonly TmPanelViewModel _tmPanelViewModel;
    private readonly IGhostTextCoordinator? _coordinator;
    private readonly IGrammarCheckerService? _grammarChecker;
    private readonly IAmtaLinterService? _amtaLinter;
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

        _bridgeService = App.Services.GetRequiredService<IWebViewBridge>();
        _viewModel = App.Services.GetRequiredService<WorkspaceViewModel>();
        _tmPanelViewModel = App.Services.GetRequiredService<TmPanelViewModel>();
        _coordinator = App.Services.GetService<IGhostTextCoordinator>();
        _grammarChecker = App.Services.GetService<IGrammarCheckerService>();
        _amtaLinter = App.Services.GetService<IAmtaLinterService>();
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

        // Subscribe to ghost text coordinator events (Phase 3)
        if (_coordinator is not null)
        {
            _coordinator.SuggestionReady += OnGhostTextSuggestionReady;
            _coordinator.ClearSuggestion += OnGhostTextClearSuggestion;
        }

        // Phase 4: Subscribe to grammar state changes
        if (_grammarChecker is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        // Phase 4: Subscribe to AMTA lint results
        if (_amtaLinter is not null)
        {
            _amtaLinter.LintCompleted += OnAmtaLintCompleted;
        }

        // Update TM panel state from RAG pipeline
        _tmPanelViewModel.UpdateFromPipelineState();

        // Set initial source text in the Monaco editor
        await _bridgeService.SetTextAsync("source", _viewModel.SourceText);
    }

    private void WorkspacePage_Unloaded(object sender, RoutedEventArgs e)
    {
        // Unregister messenger handlers
        WeakReferenceMessenger.Default.UnregisterAll(this);

        // Unsubscribe from coordinator events
        if (_coordinator is not null)
        {
            _coordinator.SuggestionReady -= OnGhostTextSuggestionReady;
            _coordinator.ClearSuggestion -= OnGhostTextClearSuggestion;
        }

        // Phase 4: Unsubscribe from grammar/lint events
        if (_grammarChecker is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (_amtaLinter is not null)
        {
            _amtaLinter.LintCompleted -= OnAmtaLintCompleted;
        }
    }

    /// <summary>
    /// Register messenger handlers for cursor/text events from the WebView2 bridge.
    /// Phase 2: Routes events through the ViewModel for RAG integration.
    /// Phase 3: Routes events to GhostTextCoordinator for LLM channels.
    /// Phase 4: Routes grammar/lint events through ViewModel with debounce.
    /// </summary>
    private void RegisterMessengerHandlers()
    {
        // Target cursor changes → trigger RAG lookup + LLM ghost text
        WeakReferenceMessenger.Default.Register<TargetCursorChangedMessage>(this, async (r, msg) =>
        {
            _logger.LogDebug("[RDAT] Target cursor: L{Line}:C{Col}", msg.LineNumber, msg.Column);

            // Update ViewModel
            _viewModel.TargetEditor.CursorLine = msg.LineNumber;
            _viewModel.TargetEditor.CursorColumn = msg.Column;

            // Phase 2 + Phase 3: Trigger RAG lookup + LLM channels
            await _viewModel.OnTargetCursorChangedAsync(msg.LineNumber, msg.Column);

            // Update TM panel best match
            var sourceSentence = _viewModel.SourceEditor.GetSourceSentence(msg.LineNumber);
            await _tmPanelViewModel.UpdateBestMatchAsync(sourceSentence);

            // Push RAG match as ghost text (if available)
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

        // Target text changes → forward to ViewModel + coordinator + grammar/lint
        WeakReferenceMessenger.Default.Register<TargetTextChangedMessage>(this, async (r, msg) =>
        {
            await _viewModel.OnTargetTextChangedAsync(msg.Text);
        });

        // Source text changes → forward to ViewModel + coordinator (prefetch)
        WeakReferenceMessenger.Default.Register<SourceTextChangedMessage>(this, async (r, msg) =>
        {
            await _viewModel.OnSourceTextChangedAsync(msg.Text);
        });
    }

    // ─── Phase 3: Ghost Text Event Handlers ─────────────────────────

    /// <summary>
    /// Handle a ghost text suggestion from the coordinator.
    /// Route it to the appropriate Monaco command based on channel.
    /// </summary>
    private async void OnGhostTextSuggestionReady(object? sender, GhostTextSuggestion suggestion)
    {
        await DispatcherQueue.EnqueueAsync(async () =>
        {
            switch (suggestion.Channel)
            {
                case "burst":
                    await _bridgeService.PostCommandAsync("target", "setBurstSuggestion", new
                    {
                        text = suggestion.InsertText,
                        providerId = suggestion.ProviderId,
                        label = suggestion.Label
                    });
                    break;

                case "pause":
                    await _bridgeService.PostCommandAsync("target", "setPauseSuggestion", new
                    {
                        text = suggestion.InsertText,
                        providerId = suggestion.ProviderId,
                        label = suggestion.Label
                    });
                    break;

                case "prefetch":
                    // Prefetch results are cached — don't push immediately
                    _logger.LogDebug("[RDAT] Prefetch cached: \"{Text}\"",
                        suggestion.InsertText.Length > 30 ? suggestion.InsertText[..30] + "..." : suggestion.InsertText);
                    break;

                default:
                    _logger.LogWarning("[RDAT] Unknown suggestion channel: {Channel}", suggestion.Channel);
                    break;
            }

            // Trigger inline suggestion display in Monaco
            await _bridgeService.TriggerInlineSuggestAsync("target");
        });
    }

    /// <summary>
    /// Handle a clear suggestion request from the coordinator.
    /// </summary>
    private async void OnGhostTextClearSuggestion(object? sender, string channel)
    {
        await DispatcherQueue.EnqueueAsync(async () =>
        {
            if (channel == "all" || channel == "burst")
            {
                await _bridgeService.PostCommandAsync("target", "setBurstSuggestion", new { text = "" });
            }
            if (channel == "all" || channel == "pause")
            {
                await _bridgeService.PostCommandAsync("target", "setPauseSuggestion", new { text = "" });
            }
            if (channel == "all")
            {
                await _bridgeService.ClearRagSuggestionAsync();
            }
        });
    }

    // ─── Phase 4: Grammar/Lint Event Handlers ────────────────────

    /// <summary>
    /// Handle ViewModel property changes to push grammar/lint markers to Monaco.
    /// </summary>
    private async void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceViewModel.GrammarIssues))
        {
            await PushGrammarMarkersAsync();
        }
    }

    /// <summary>
    /// Push grammar issue markers to Monaco editor via WebView2 bridge.
    /// </summary>
    private async void PushGrammarMarkersAsync()
    {
        await DispatcherQueue.EnqueueAsync(async () =>
        {
            var issues = _viewModel.GrammarIssues;
            if (issues.Count == 0)
            {
                await _bridgeService.ClearGrammarMarkersAsync();
                return;
            }

            var markers = issues.Select(i => new
            {
                id = i.Id,
                type = i.Type.ToString().ToLowerInvariant(),
                message = i.Message,
                suggestion = i.Suggestion,
                originalText = i.OriginalText,
                startLineNumber = i.StartLineNumber,
                endLineNumber = i.EndLineNumber,
                startColumn = i.StartColumn,
                endColumn = i.EndColumn
            }).ToArray();

            await _bridgeService.SetGrammarMarkersAsync(markers!);
        });
    }

    /// <summary>
    /// Handle AMTA lint completion — push markers to Monaco.
    /// </summary>
    private async void OnAmtaLintCompleted(object? sender, IReadOnlyList<AmtaLintIssue> issues)
    {
        await DispatcherQueue.EnqueueAsync(async () =>
        {
            if (issues.Count == 0)
            {
                await _bridgeService.ClearAmtaLintMarkersAsync();
                return;
            }

            var markers = issues.Select(i => new
            {
                id = i.Id,
                type = i.Type.ToString(),
                severity = i.Severity.ToString(),
                message = i.Message,
                suggestion = i.Suggestion,
                originalText = i.OriginalText,
                startLineNumber = i.StartLineNumber,
                endLineNumber = i.EndLineNumber,
                startColumn = i.StartColumn,
                endColumn = i.EndColumn,
                domain = i.Domain
            }).ToArray();

            await _bridgeService.SetAmtaLintMarkersAsync(markers!);
        });
    }

    /// <summary>
    /// Accept a grammar suggestion — apply quick fix to Monaco.
    /// </summary>
    private async void AcceptGrammarFix_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GrammarIssue issue })
        {
            await _bridgeService.ApplyQuickFixAsync(
                issue.StartLineNumber,
                issue.StartColumn,
                issue.EndColumn,
                issue.Suggestion);
        }
    }

    /// <summary>
    /// Accept an AMTA lint suggestion — apply quick fix to Monaco.
    /// </summary>
    private async void AcceptAmtaFix_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AmtaLintIssue issue })
        {
            await _bridgeService.ApplyQuickFixAsync(
                issue.StartLineNumber,
                issue.StartColumn,
                issue.EndColumn,
                issue.Suggestion);
        }
    }

    /// <summary>
    /// Run Gemini quality estimation.
    /// </summary>
    private async void RunQualityCheck_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.RunQualityEstimationAsync();
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
        var picker = new Windows.Storage.Pickers.FileOpenPicker();

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

    // ─── Phase 5: Document Import &amp; Multi-Window Handlers ──────

    private async void ImportDocx_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();

        var hwnd = WindowNative.GetWindowHandle(
            App.Services.GetRequiredService<MainWindow>());
        InitializeWithWindow.Initialize(picker, hwnd);

        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationLocation.DocumentsLibrary;
        picker.FileTypeFilter.Add(".docx");

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            await _viewModel.ImportDocumentCommand.ExecuteAsync(file.Path);

            // If import succeeded, update Monaco source editor
            if (_viewModel.IsDocumentLoaded)
            {
                await _bridgeService.SetTextAsync("source", _viewModel.SourceText);
            }
        }
    }

    private void NewWindow_Click(object sender, RoutedEventArgs e)
    {
        var mainWindow = App.Services.GetRequiredService<MainWindow>();
        mainWindow.OpenNewWindow();
        _logger.LogInformation("[RDAT] New window requested");
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
