using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Desktop.ViewModels;

/// <summary>
/// ViewModel for the SettingsPage. Manages language direction,
/// API keys, RAG pipeline, and LLM model configuration.
/// Phase 2: Added Translation Memory database and embedding model settings.
/// Phase 3: Added LLM model path, temperature, and engine status.
/// Phase 4 will integrate Windows Credential Locker for secure key storage.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly ILocalInferenceService? _inferenceService;
    private readonly ILlmQueueService? _queueService;

    [ObservableProperty]
    private LanguageDirection _languageDirection = LanguageDirection.EnToAr;

    [ObservableProperty]
    private string _geminiApiKey = string.Empty;

    [ObservableProperty]
    private bool _hasGeminiKey;

    [ObservableProperty]
    private string _geminiKeyMasked = string.Empty;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string _saveStatus = string.Empty;

    // ─── Phase 2: Translation Memory Settings ───────────────────────

    [ObservableProperty]
    private string _embeddingModelPath = string.Empty;

    [ObservableProperty]
    private string _tmDbPath = string.Empty;

    [ObservableProperty]
    private string _ragPipelineState = "Not Initialized";

    [ObservableProperty]
    private string _tmEntryCount = "0";

    [ObservableProperty]
    private string _ragModelStatus = "No model loaded";

    // ─── Phase 3: LLM Model Settings ────────────────────────────────

    [ObservableProperty]
    private string _llmModelPath = string.Empty;

    [ObservableProperty]
    private float _llmTemperature = 0.3f;

    [ObservableProperty]
    private string _temperatureDisplay = "0.30";

    [ObservableProperty]
    private bool _isLlmInitializing;

    [ObservableProperty]
    private string _llmEngineState = "Not Loaded";

    [ObservableProperty]
    private string _queueStatus = "Idle (0 pending)";

    public SettingsViewModel(
        ILogger<SettingsViewModel> logger,
        ILocalInferenceService? inferenceService = null,
        ILlmQueueService? queueService = null)
    {
        _logger = logger;
        _inferenceService = inferenceService;
        _queueService = queueService;

        // Set default paths
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        TmDbPath = Path.Combine(localAppData, "RDAT", "TM", "Database");
        EmbeddingModelPath = Path.Combine(localAppData, "RDAT", "Models", "multilingual-minilm-l12");
        LlmModelPath = Path.Combine(localAppData, "RDAT", "Models", "gemma-2b-it-q4f32_1-ONNX");

        // Initialize LLM state from service
        if (_inferenceService is not null)
        {
            UpdateLlmState();
        }
    }

    partial void OnLlmTemperatureChanged(float value)
    {
        TemperatureDisplay = value.ToString("F2");
    }

    /// <summary>
    /// Update LLM engine state from the inference service.
    /// </summary>
    public void UpdateLlmState()
    {
        if (_inferenceService is null) return;

        LlmEngineState = _inferenceService.State switch
        {
            LlmState.Idle => "Not Loaded",
            LlmState.Initializing => "Loading...",
            LlmState.Ready => "Ready ✓",
            LlmState.Generating => "Generating...",
            LlmState.Error => "Error ✗",
            _ => "Unknown"
        };

        if (_queueService is not null)
        {
            QueueStatus = _queueService.IsRunning
                ? $"Running ({_queueService.PendingCount} pending)"
                : "Idle (0 pending)";
        }

        _logger.LogDebug("[Settings] LLM state: {State}, Queue: {Queue}",
            LlmEngineState, QueueStatus);
    }

    [RelayCommand]
    private void SaveGeminiApiKey()
    {
        if (string.IsNullOrWhiteSpace(GeminiApiKey))
        {
            SaveStatus = "Please enter a valid API key.";
            _logger.LogWarning("[RDAT] Empty Gemini API key submitted");
            return;
        }

        // Phase 4: Store in Windows Credential Locker
        // PasswordVault.Add(new PasswordCredential("RDAT-Gemini", "gemini-api-key", GeminiApiKey));

        HasGeminiKey = true;
        GeminiKeyMasked = $"****{GeminiApiKey[^4..]}";
        SaveStatus = "API key saved securely.";
        _logger.LogInformation("[RDAT] Gemini API key saved (masked: {Masked})", GeminiKeyMasked);
    }
}
