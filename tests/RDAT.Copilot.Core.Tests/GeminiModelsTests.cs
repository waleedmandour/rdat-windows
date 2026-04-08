using RDAT.Copilot.Core.Models;
using Xunit;

namespace RDAT.Copilot.Core.Tests;

/// <summary>
/// Unit tests for the Gemini Cloud AI models (Phase 4).
/// Tests GeminiState, QualityEstimationResult, and GeminiRateLimit.
/// </summary>
public class GeminiModelsTests
{
    [Fact]
    public void GeminiState_AllValuesDefined()
    {
        Assert.Equal(5, Enum.GetValues<GeminiState>().Length);
        Assert.Contains(GeminiState.NotConfigured, Enum.GetValues<GeminiState>());
        Assert.Contains(GeminiState.Configured, Enum.GetValues<GeminiState>());
        Assert.Contains(GeminiState.Ready, Enum.GetValues<GeminiState>());
        Assert.Contains(GeminiState.Busy, Enum.GetValues<GeminiState>());
        Assert.Contains(GeminiState.Error, Enum.GetValues<GeminiState>());
    }

    [Fact]
    public void QualityEstimationResult_CreatesWithAllScores()
    {
        var result = new QualityEstimationResult(
            OverallScore: 85,
            AccuracyScore: 90,
            FluencyScore: 88,
            StyleScore: 80,
            TerminologyScore: 82,
            Feedback: "Good translation with minor terminology issues.",
            EstimatedMs: 1250.5
        );

        Assert.Equal(85, result.OverallScore);
        Assert.Equal(90, result.AccuracyScore);
        Assert.Equal(88, result.FluencyScore);
        Assert.Equal(80, result.StyleScore);
        Assert.Equal(82, result.TerminologyScore);
        Assert.Contains("terminology", result.Feedback);
        Assert.Equal(1250.5, result.EstimatedMs);
    }

    [Fact]
    public void QualityEstimationResult_HandlesZeroScores()
    {
        var result = new QualityEstimationResult(
            OverallScore: 0,
            AccuracyScore: 0,
            FluencyScore: 0,
            StyleScore: 0,
            TerminologyScore: 0,
            Feedback: "No translation provided.",
            EstimatedMs: 0
        );

        Assert.Equal(0, result.OverallScore);
        Assert.Equal(0, result.AccuracyScore);
        Assert.Equal(0, result.FluencyScore);
        Assert.Equal(0, result.StyleScore);
        Assert.Equal(0, result.TerminologyScore);
    }

    [Fact]
    public void QualityEstimationResult_PerfectScore()
    {
        var result = new QualityEstimationResult(
            OverallScore: 100,
            AccuracyScore: 100,
            FluencyScore: 100,
            StyleScore: 100,
            TerminologyScore: 100,
            Feedback: "Excellent translation.",
            EstimatedMs: 890.0
        );

        Assert.Equal(100, result.OverallScore);
        Assert.All(new[] { result.AccuracyScore, result.FluencyScore, result.StyleScore, result.TerminologyScore },
            score => Assert.Equal(100, score));
    }

    [Fact]
    public void GeminiRateLimit_TracksRemainingRequests()
    {
        var rateLimit = new GeminiRateLimit(
            RequestsPerMinute: 15,
            RemainingRequests: 12,
            ResetAtUnixMs: DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds()
        );

        Assert.Equal(15, rateLimit.RequestsPerMinute);
        Assert.Equal(12, rateLimit.RemainingRequests);
        Assert.True(rateLimit.ResetAtUnixMs > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }
}
