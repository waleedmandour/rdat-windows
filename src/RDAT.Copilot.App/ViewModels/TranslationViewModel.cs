using System;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RDAT.Copilot.Core.Models;
using RDAT.Copilot.Core.Orchestration;
using RDAT.Copilot.Infrastructure.Monaco;

namespace RDAT.Copilot.App.ViewModels;

public sealed partial class TranslationViewModel : ObservableObject, IDisposable
{
    private readonly GhostTextCoordinator _coordinator;
    private readonly EditorBridge _editorBridge;
    private readonly ILogger<TranslationViewModel> _logger;
    private IDisposable? _predictionSubscription;

    [ObservableProperty]
    private string _activeSourceSegment = string.Empty;

    [ObservableProperty]
    private string _activeTargetSegment = string.Empty;

    [ObservableProperty]
    private bool _isRtlDirection = true; // Default to Arabic target

    public ObservableCollection<string> ExtractedSegments { get; } = new();

    public TranslationViewModel(
        GhostTextCoordinator coordinator, 
        EditorBridge editorBridge, 
        ILogger<TranslationViewModel> logger)
    {
        _coordinator = coordinator;
        _editorBridge = editorBridge;
        _logger = logger;

        // Note: The EditorBridge handles pushing GhostText to Monaco natively.
        // But if the ViewModel needs UI visibility (e.g. status bar info):
        _predictionSubscription = _coordinator.GhostTextStream
            .ObserveOn(SynchronizationContext.Current ?? new SynchronizationContext())
            .Subscribe(result =>
            {
                // UI could listen here for typing suggestion available state.
                OnPropertyChanged(nameof(HasPrediction));
            });
    }

    public bool HasPrediction => true; // Could rely on state from GhostTextResult if cached

    [RelayCommand]
    private void ToggleDirection()
    {
        IsRtlDirection = !IsRtlDirection;
        // In a real application, you'd signal the Monaco bridge to switch direction via PostMessage
        // Example: _editorBridge.SetRtl(IsRtlDirection);
    }

    [RelayCommand]
    private async Task OpenDocumentAsync(string filePath)
    {
        try
        {
            ExtractedSegments.Clear();
            
            // Mock OpenXml Document Parsing Loop
            // Normally OpenXml PowerTools or DocumentFormat.OpenXml is used
            ExtractedSegments.Add("Overview of the latest technical specifications.");
            ExtractedSegments.Add("Ensure all connections are tight and secure.");
            ExtractedSegments.Add("Always reboot the system post-installation.");

            if (ExtractedSegments.Count > 0)
            {
                ActiveSourceSegment = ExtractedSegments[0];
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open document: {FilePath}", filePath);
        }
        
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        _predictionSubscription?.Dispose();
        _editorBridge.Dispose();
        _coordinator.Dispose();
    }
}
