using System;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Models;
using RDAT.Copilot.Core.Services;

namespace RDAT.Copilot.App.ViewModels;

/// <summary>
/// Main workspace ViewModel that orchestrates the translation editing experience.
/// Subscribes to the GhostTextCoordinator pipeline and manages document segments,
/// RTL/LTR direction, and TM search commands.
/// </summary>
public sealed partial class TranslationViewModel : ObservableObject, IDisposable
{
    private readonly GhostTextCoordinator _coordinator;
    private readonly IEditorBridge _editorBridge;
    private readonly ILogger<TranslationViewModel> _logger;
    private IDisposable? _predictionSubscription;

    [ObservableProperty]
    private string _activeSourceSegment = string.Empty;

    [ObservableProperty]
    private string _activeTargetSegment = string.Empty;

    [ObservableProperty]
    private bool _isRtlDirection = true; // Default to Arabic target

    [ObservableProperty]
    private bool _hasPrediction;

    [ObservableProperty]
    private string _predictionText = string.Empty;

    public ObservableCollection<string> ExtractedSegments { get; } = new();

    public TranslationViewModel(
        GhostTextCoordinator coordinator,
        IEditorBridge editorBridge,
        ILogger<TranslationViewModel> logger)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _editorBridge = editorBridge ?? throw new ArgumentNullException(nameof(editorBridge));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to ghost text predictions for UI state updates
        _predictionSubscription = _coordinator.GhostTextStream
            .ObserveOn(SynchronizationContext.Current ?? new SynchronizationContext())
            .Subscribe(result =>
            {
                HasPrediction = !string.IsNullOrEmpty(result?.Text) && !result.IsSuppressed;
                PredictionText = result?.Text ?? "";
                OnPropertyChanged(nameof(HasPrediction));
            });
    }

    [RelayCommand]
    private void ToggleDirection()
    {
        IsRtlDirection = !IsRtlDirection;
        _editorBridge.SetDirection(IsRtlDirection);
        _logger.LogDebug("Direction toggled to {Direction}", IsRtlDirection ? "RTL" : "LTR");
    }

    [RelayCommand]
    private async Task OpenDocumentAsync(string filePath)
    {
        try
        {
            ExtractedSegments.Clear();

            // Mock OpenXml document parsing — in production, use DocumentFormat.OpenXml
            ExtractedSegments.Add("Overview of the latest technical specifications.");
            ExtractedSegments.Add("Ensure all connections are tight and secure.");
            ExtractedSegments.Add("Always reboot the system post-installation.");

            if (ExtractedSegments.Count > 0)
            {
                ActiveSourceSegment = ExtractedSegments[0];
            }

            _logger.LogInformation("Opened document with {Count} segments", ExtractedSegments.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open document: {FilePath}", filePath);
        }

        await Task.CompletedTask;
    }

    [RelayCommand]
    private void AcceptPrediction()
    {
        _editorBridge.ClearGhostText();
        HasPrediction = false;
        PredictionText = "";
    }

    [RelayCommand]
    private void RejectPrediction()
    {
        _editorBridge.ClearGhostText();
        HasPrediction = false;
        PredictionText = "";
    }

    public void Dispose()
    {
        _predictionSubscription?.Dispose();
        // Note: Do NOT dispose _editorBridge or _coordinator — they're owned by DI
    }
}
