using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Desktop.ViewModels;

/// <summary>
/// ViewModel for the SettingsPage. Manages language direction,
/// API keys, RAG pipeline, LLM model, and Phase 4 services configuration.
/// Phase 2: Added Translation Memory database and embedding model settings.
/// Phase 3: Added LLM model path, temperature, and engine status.
/// Phase 4: Added Gemini API key (Credential Locker), AMTA glossary, grammar/lint toggles.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly ILocalInferenceService? _inferenceService;
    private readonly ILlmQueueService? _queueService;
    private readonly IGeminiCloudService? _geminiService;
    private readonly IAmtaLinterService? _amtaLinter;

    [ObservableProperty]
    private LanguageDirection _languageDirection = LanguageDirection.EnToAr;

    // ─── Gemini API Key ─────────────────────────────────────────

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

    // ─── Phase 2: Translation Memory Settings ────────────────────

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

    // ─── Phase 3: LLM Model Settings ─────────────────────────────

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

    // ─── Phase 4: Gemini Cloud Settings ──────────────────────────

    [ObservableProperty]
    private string _geminiStateDisplay = "Not Configured";

    [ObservableProperty]
    private bool _isGeminiValidating;

    // ─── Phase 4: AMTA Glossary Settings ─────────────────────────

    [ObservableProperty]
    private string _glossaryPath = string.Empty;

    [ObservableProperty]
    private string _amtaLinterState = "No Glossary";

    [ObservableProperty]
    private string _amtaTermCountDisplay = "0 terms";

    [ObservableProperty]
    private bool _isGlossaryLoading;

    // ─── Phase 4: Grammar/Lint Toggles ───────────────────────────

    [ObservableProperty]
    private bool _isGrammarEnabled = true;

    [ObservableProperty]
    private bool _isAmtaLintEnabled = true;

    [ObservableProperty]
    private bool _isGeminiGrammarEnabled;

    public SettingsViewModel(
        ILogger<SettingsViewModel> logger,
        ILocalInferenceService? inferenceService = null,
        ILlmQueueService? queueService = null,
        IGeminiCloudService? geminiService = null,
        IAmtaLinterService? amtaLinter = null)
    {
        _logger = logger;
        _inferenceService = inferenceService;
        _queueService = queueService;
        _geminiService = geminiService;
        _amtaLinter = amtaLinter;

        // Set default paths
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        TmDbPath = Path.Combine(localAppData, "RDAT", "TM", "Database");
        EmbeddingModelPath = Path.Combine(localAppData, "RDAT", "Models", "multilingual-minilm-l12");
        LlmModelPath = Path.Combine(localAppData, "RDAT", "Models", "gemma-2b-it-q4f32_1-ONNX");
        GlossaryPath = Path.Combine(localAppData, "RDAT", "Glossaries");

        // Initialize LLM state from service
        if (_inferenceService is not null)
        {
            UpdateLlmState();
        }

        // Initialize Gemini state from service
        if (_geminiService is not null)
        {
            UpdateGeminiState();
        }

        // Initialize AMTA linter state from service
        if (_amtaLinter is not null)
        {
            UpdateAmtaState();
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

    /// <summary>
    /// Update Gemini cloud service state.
    /// </summary>
    public void UpdateGeminiState()
    {
        if (_geminiService is null) return;

        GeminiStateDisplay = _geminiService.State switch
        {
            GeminiState.NotConfigured => "Not Configured",
            GeminiState.Configured => "Key Set (unvalidated)",
            GeminiState.Ready => "Connected ✓",
            GeminiState.Busy => "Processing...",
            GeminiState.Error => "Error ✗",
            _ => "Unknown"
        };

        HasGeminiKey = _geminiService.IsConfigured;
        _logger.LogDebug("[Settings] Gemini state: {State}", GeminiStateDisplay);
    }

    /// <summary>
    /// Update AMTA linter state.
    /// </summary>
    public void UpdateAmtaState()
    {
        if (_amtaLinter is null) return;

        AmtaLinterState = _amtaLinter.State switch
        {
            AmtaLinterState.Idle => "No Glossary Loaded",
            AmtaLinterState.Loading => "Loading...",
            AmtaLinterState.Ready => "Ready",
            AmtaLinterState.Checking => "Checking...",
            AmtaLinterState.Error => "Error",
            _ => "Unknown"
        };

        AmtaTermCountDisplay = $"{_amtaLinter.TermCount} terms";
        _logger.LogDebug("[Settings] AMTA state: {State}", AmtaLinterState);
    }

    // ─── Gemini API Key Commands ─────────────────────────────────

    [RelayCommand]
    private async Task SaveGeminiApiKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(GeminiApiKey))
        {
            SaveStatus = "Please enter a valid API key.";
            _logger.LogWarning("[RDAT] Empty Gemini API key submitted");
            return;
        }

        IsSaving = true;
        SaveStatus = "Validating Gemini API key...";

        try
        {
            if (_geminiService is not null)
            {
                var isValid = await _geminiService.ConfigureAsync(GeminiApiKey).ConfigureAwait(true);
                if (isValid)
                {
                    HasGeminiKey = true;
                    GeminiKeyMasked = $"****{GeminiApiKey[^4..]}";
                    SaveStatus = "API key saved and validated successfully.";
                    UpdateGeminiState();
                }
                else
                {
                    SaveStatus = "API key validation failed. Check the key and try again.";
                }
            }
        }
        catch (Exception ex)
        {
            SaveStatus = $"Failed: {ex.Message}";
            _logger.LogError(ex, "[RDAT] Gemini API key save failed");
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task RemoveGeminiApiKeyAsync()
    {
        if (_geminiService is null) return;

        await _geminiService.RemoveApiKeyAsync().ConfigureAwait(true);
        HasGeminiKey = false;
        GeminiKeyMasked = string.Empty;
        GeminiApiKey = string.Empty;
        SaveStatus = "Gemini API key removed.";
        UpdateGeminiState();
        _logger.LogInformation("[RDAT] Gemini API key removed");
    }

    [RelayCommand]
    private async Task ValidateGeminiKeyAsync()
    {
        if (_geminiService is null) return;

        IsGeminiValidating = true;
        SaveStatus = "Validating API key...";

        try
        {
            var isValid = await _geminiService.ValidateKeyAsync().ConfigureAwait(true);
            SaveStatus = isValid ? "API key is valid." : "API key validation failed.";
            UpdateGeminiState();
        }
        catch (Exception ex)
        {
            SaveStatus = $"Validation error: {ex.Message}";
        }
        finally
        {
            IsGeminiValidating = false;
        }
    }
}
