// ========================================================================
// RDAT Copilot - Gemini Cloud Fallback Service Interface
// Location: src/RDAT.Copilot.Core/Interfaces/
// ========================================================================

using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Core.Interfaces;

/// <summary>
/// Cloud-based Gemini API fallback for translation when local ONNX
/// inference is unavailable or insufficient. Provides high-quality
/// translations via Google's Generative AI API.
/// </summary>
public interface IGeminiService
{
    /// <summary>True when the API key is configured and the service is ready.</summary>
    bool IsConfigured { get; }

    /// <summary>Generates a ghost text prediction using Gemini cloud API.</summary>
    Task<GhostTextResult> GetPredictionAsync(
        string sourceText,
        string sourceLang = "en",
        string targetLang = "ar",
        CancellationToken cancellationToken = default);

    /// <summary>Generates a full translation using Gemini cloud API.</summary>
    Task<GhostTextResult> GetFullTranslationAsync(
        string sourceText,
        string sourceLang = "en",
        string targetLang = "ar",
        CancellationToken cancellationToken = default);

    /// <summary>Sets the Gemini API key for authentication.</summary>
    void Configure(string apiKey);
}
