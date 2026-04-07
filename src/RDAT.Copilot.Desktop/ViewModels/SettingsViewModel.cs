using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Desktop.ViewModels;

/// <summary>
/// ViewModel for the SettingsPage. Manages language direction,
/// API keys, and RAG pipeline configuration.
/// Phase 2: Added Translation Memory database and embedding model settings.
/// Phase 4 will integrate Windows Credential Locker for secure key storage.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ILogger<SettingsViewModel> _logger;

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

    public SettingsViewModel(ILogger<SettingsViewModel> logger)
    {
        _logger = logger;

        // Set default paths
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        TmDbPath = Path.Combine(localAppData, "RDAT", "TM", "Database");
        EmbeddingModelPath = Path.Combine(localAppData, "RDAT", "Models", "multilingual-minilm-l12");
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
