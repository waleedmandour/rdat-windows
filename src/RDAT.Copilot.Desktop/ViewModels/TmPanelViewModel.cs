using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Translation Memory panel.
/// Manages TM import, search, and browse operations.
/// Displays TM match results and provides interaction with the RAG pipeline.
/// </summary>
public partial class TmPanelViewModel : ObservableObject
{
    private readonly IRagPipelineService _ragPipeline;
    private readonly ILogger<TmPanelViewModel> _logger;

    [ObservableProperty]
    private bool _isPanelOpen;

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string _importFilePath = string.Empty;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "No TM loaded";

    [ObservableProperty]
    private string _importProgress = string.Empty;

    [ObservableProperty]
    private int _importPercent;

    [ObservableProperty]
    private long _totalTmCount;

    [ObservableProperty]
    private double _tmDbSizeMb;

    [ObservableProperty]
    private string _ragStateDisplay = "Idle";

    [ObservableProperty]
    private string _bestMatchSource = string.Empty;

    [ObservableProperty]
    private string _bestMatchTarget = string.Empty;

    [ObservableProperty]
    private double _bestMatchScore;

    [ObservableProperty]
    private bool _hasBestMatch;

    /// <summary>
    /// Collection of TM search results for the panel's ListView.
    /// </summary>
    public ObservableCollection<TmSearchResult> SearchResults { get; } = new();

    public TmPanelViewModel(
        IRagPipelineService ragPipeline,
        ILogger<TmPanelViewModel> logger)
    {
        _ragPipeline = ragPipeline;
        _logger = logger;
    }

    /// <summary>
    /// Update the TM panel state from the RAG pipeline.
    /// Called when the RAG pipeline state changes.
    /// </summary>
    public void UpdateFromPipelineState()
    {
        TotalTmCount = _ragPipeline.TotalTmCount;
        RAGStateDisplay = _ragPipeline.State.ToString();

        if (_ragPipeline.IsReady && _ragPipeline.TotalTmCount > 0)
        {
            StatusMessage = $"{_ragPipeline.TotalTmCount:N0} TM entries loaded";
        }
        else if (_ragPipeline.IsReady)
        {
            StatusMessage = "TM ready — Import a file to begin";
        }
        else
        {
            StatusMessage = "TM not initialized";
        }
    }

    /// <summary>
    /// Update the best TM match display for the current source sentence.
    /// Called from the WorkspaceViewModel when the cursor moves to a new source line.
    /// </summary>
    public async Task UpdateBestMatchAsync(string sourceText)
    {
        if (!_ragPipeline.IsReady || string.IsNullOrWhiteSpace(sourceText))
        {
            HasBestMatch = false;
            return;
        }

        try
        {
            var bestMatch = await _ragPipeline.GetBestMatchAsync(sourceText).ConfigureAwait(true);
            if (bestMatch is not null && bestMatch.Score >= 0.7)
            {
                BestMatchSource = bestMatch.Entry.SourceText;
                BestMatchTarget = bestMatch.Entry.TargetText;
                BestMatchScore = bestMatch.Score;
                HasBestMatch = true;
            }
            else
            {
                HasBestMatch = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[TM-Panel] Best match lookup failed");
            HasBestMatch = false;
        }
    }

    [RelayCommand]
    private async Task ImportTmFileAsync()
    {
        if (string.IsNullOrWhiteSpace(ImportFilePath) || !File.Exists(ImportFilePath))
        {
            StatusMessage = "Invalid file path";
            return;
        }

        IsImporting = true;
        ImportProgress = "Starting import...";
        ImportPercent = 0;
        StatusMessage = "Importing...";

        try
        {
            var progress = new Progress<(int Imported, int Total, string Text)>(p =>
            {
                ImportPercent = p.Total > 0 ? (int)((double)p.Imported / p.Total * 100) : 0;
                ImportProgress = p.Text;
            });

            var result = await _ragPipeline.ImportTmFileAsync(
                ImportFilePath,
                progress: progress).ConfigureAwait(true);

            StatusMessage = $"Imported {result.ImportedCount:N0} / {result.TotalRows:N0} entries " +
                           $"({result.ElapsedMs:F0}ms)";
            ImportProgress = $"Done! {result.ErrorCount} errors.";

            UpdateFromPipelineState();
            _logger.LogInformation("[TM-Panel] Import result: {Imported}/{Total}", 
                result.ImportedCount, result.TotalRows);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Import cancelled";
            ImportProgress = "Cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
            _logger.LogError(ex, "[TM-Panel] TM import failed");
        }
        finally
        {
            IsImporting = false;
        }
    }

    [RelayCommand]
    private async Task SearchTmAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery) || !_ragPipeline.IsReady)
        {
            SearchResults.Clear();
            return;
        }

        IsSearching = true;
        StatusMessage = "Searching...";

        try
        {
            var results = await _ragPipeline.SearchTmAsync(SearchQuery, topK: 10).ConfigureAwait(true);

            SearchResults.Clear();
            foreach (var result in results)
            {
                SearchResults.Add(result);
            }

            StatusMessage = $"{results.Count} matches found";
            _logger.LogInformation("[TM-Panel] Search returned {Count} results", results.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Search failed: {ex.Message}";
            _logger.LogError(ex, "[TM-Panel] TM search failed");
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task RefreshStatsAsync()
    {
        try
        {
            var stats = await _ragPipeline.GetStatsAsync().ConfigureAwait(true);
            TotalTmCount = stats.TotalEntries;
            TmDbSizeMb = stats.DbSizeMb;
            StatusMessage = $"{stats.TotalEntries:N0} entries ({stats.DbSizeMb:F1} MB)";
            UpdateFromPipelineState();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TM-Panel] Failed to refresh stats");
        }
    }

    [RelayCommand]
    private void TogglePanel()
    {
        IsPanelOpen = !IsPanelOpen;
        _logger.LogDebug("[TM-Panel] Panel toggled: {Open}", IsPanelOpen);
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQuery = string.Empty;
        SearchResults.Clear();
    }

    [RelayCommand]
    private void ClearBestMatch()
    {
        HasBestMatch = false;
        BestMatchSource = string.Empty;
        BestMatchTarget = string.Empty;
    }
}
