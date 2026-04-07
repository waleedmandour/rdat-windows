namespace RDAT.Copilot.Core.Models;

/// <summary>
/// State of the Gemini Cloud AI service.
/// </summary>
public enum GeminiState
{
    /// <summary>Not configured (no API key).</summary>
    NotConfigured,

    /// <summary>API key configured but not yet validated.</summary>
    Configured,

    /// <summary>API key validated and service is ready.</summary>
    Ready,

    /// <summary>A request is currently in-flight.</summary>
    Busy,

    /// <summary>API call failed (network, rate limit, auth error).</summary>
    Error
}

/// <summary>
/// Result of a Gemini quality estimation analysis.
/// Provides a holistic quality score (0-100) with categorical breakdowns
/// for accuracy, fluency, style, and terminology adherence.
/// </summary>
/// <param name="OverallScore">Overall quality score from 0 to 100.</param>
/// <param name="AccuracyScore">Accuracy score (how faithful to the source).</param>
/// <param name="FluencyScore">Fluency score (how natural the target reads).</param>
/// <param name="StyleScore">Style score (register and tone appropriateness).</param>
/// <param name="TerminologyScore">Terminology score (glossary adherence).</param>
/// <param name="Feedback">Detailed feedback and improvement suggestions.</param>
/// <param name="EstimatedMs">Time taken for quality estimation in milliseconds.</param>
public record QualityEstimationResult(
    int OverallScore,
    int AccuracyScore,
    int FluencyScore,
    int StyleScore,
    int TerminologyScore,
    string Feedback,
    double EstimatedMs
);

/// <summary>
/// Gemini API rate limit tracking state.
/// </summary>
public record GeminiRateLimit(
    int RequestsPerMinute,
    int RemainingRequests,
    long ResetAtUnixMs
);
