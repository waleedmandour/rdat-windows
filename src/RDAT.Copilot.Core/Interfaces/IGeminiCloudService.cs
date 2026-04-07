using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Core.Interfaces;

/// <summary>
/// Contract for the Gemini Cloud AI service (Phase 4).
/// Provides BYOK (Bring Your Own Key) integration with Google Gemini 2.0 Flash
/// for enhanced quality estimation, style scoring, cloud-fallback grammar checking,
/// and translation rewriting capabilities.
///
/// The service handles:
///   - API key management (via ICredentialService)
///   - Rate limiting and retry logic
///   - Structured JSON output parsing
///   - Quality estimation and style analysis
/// </summary>
public interface IGeminiCloudService
{
    /// <summary>Current Gemini service state.</summary>
    GeminiState State { get; }

    /// <summary>Whether the service has a valid API key configured.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Configure the Gemini API key.
    /// The key is stored in the credential service, not in memory.
    /// </summary>
    /// <param name="apiKey">The Gemini API key.</param>
    /// <returns>True if the key was validated successfully.</returns>
    Task<bool> ConfigureAsync(string apiKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate the currently stored API key by making a lightweight test call.
    /// </summary>
    Task<bool> ValidateKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Run grammar/spell/style check using Gemini.
    /// Provides higher-quality results than the local LLM for complex grammatical
    /// issues, especially for Arabic morphological analysis.
    /// </summary>
    /// <param name="text">Target translation text to check.</param>
    /// <param name="sourceSentence">Source sentence for context.</param>
    /// <param name="languageDirection">EN→AR or AR→EN.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of grammar issues detected by Gemini.</returns>
    Task<IReadOnlyList<GrammarIssue>> CheckGrammarAsync(
        string text,
        string sourceSentence,
        LanguageDirection languageDirection,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimate the quality of a translation segment.
    /// Returns a quality score (0-100) with detailed feedback.
    /// </summary>
    /// <param name="sourceText">Source text.</param>
    /// <param name="targetText">Target translation text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Quality estimation result with score and feedback.</returns>
    Task<QualityEstimationResult> EstimateQualityAsync(
        string sourceText,
        string targetText,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rewrite a translation segment with improved style or register.
    /// Useful for style normalization and tone adjustment.
    /// </summary>
    /// <param name="sourceText">Source text.</param>
    /// <param name="targetText">Current target translation.</param>
    /// <param name="styleHint">Style instruction (e.g., "formal", "natural", "academic").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Rewritten translation text.</returns>
    Task<string?> RewriteAsync(
        string sourceText,
        string targetText,
        string styleHint = "formal",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove the stored API key.
    /// </summary>
    Task RemoveApiKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised when the Gemini service state changes.
    /// </summary>
    event EventHandler<GeminiState>? StateChanged;
}
