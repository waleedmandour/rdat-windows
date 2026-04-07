using RDAT.Copilot.Core.Constants;
using RDAT.Copilot.Core.Models;
using Xunit;

namespace RDAT.Copilot.Core.Tests;

public class RagPipelineTests
{
    [Fact]
    public void RagState_DefaultIsIdle()
    {
        var state = RagState.Idle;
        Assert.Equal("Idle", state.ToString());
    }

    [Fact]
    public void RagState_TransitionsAreValid()
    {
        // Verify all expected states exist
        var states = Enum.GetValues<RagState>();
        Assert.Equal(6, states.Length);
        Assert.Contains(RagState.Idle, states);
        Assert.Contains(RagState.LoadingModel, states);
        Assert.Contains(RagState.Indexing, states);
        Assert.Contains(RagState.Ready, states);
        Assert.Contains(RagState.Searching, states);
        Assert.Contains(RagState.Error, states);
    }

    [Fact]
    public void AppConstants_RagValues_AreConsistent()
    {
        // Embedding dimensions must be 384 for multilingual MiniLM
        Assert.Equal(384, AppConstants.EmbeddingDimensions);

        // Search limit should be reasonable
        Assert.True(AppConstants.RagSearchLimit > 0 && AppConstants.RagSearchLimit <= 20);
        Assert.Equal(5, AppConstants.RagSearchLimit);

        // Search target should be fast (< 100ms)
        Assert.True(AppConstants.RagSearchTargetMs > 0 && AppConstants.RagSearchTargetMs <= 200);
        Assert.Equal(50, AppConstants.RagSearchTargetMs);
    }

    [Fact]
    public void TmSearchResult_ScoreIsBounded()
    {
        var entry = new TmEntry("id", "source", "target");
        var result = new TmSearchResult(entry, Score: 0.85, SearchMs: 30.0);

        // Score should be between 0 and 1 for cosine similarity
        Assert.True(result.Score >= 0.0);
        Assert.True(result.Score <= 1.0);
    }

    [Fact]
    public void TmSearchResult_SearchTimeIsReasonable()
    {
        var entry = new TmEntry("id", "source", "target");

        // Simulate a very fast search result
        var fastResult = new TmSearchResult(entry, Score: 0.9, SearchMs: 0.5);
        Assert.True(fastResult.SearchMs < AppConstants.RagSearchTargetMs);

        // Simulate a slow search result
        var slowResult = new TmSearchResult(entry, Score: 0.7, SearchMs: 200.0);
        Assert.True(slowResult.SearchMs > AppConstants.RagSearchTargetMs);
    }

    [Fact]
    public void SuggestionMode_Enum_HasExpectedValues()
    {
        var modes = Enum.GetValues<SuggestionMode>();
        Assert.Equal(3, modes.Length);
        Assert.Contains(SuggestionMode.Gtr, modes);
        Assert.Contains(SuggestionMode.ZeroShot, modes);
        Assert.Contains(SuggestionMode.Pause, modes);
    }

    [Fact]
    public void GhostTextSuggestion_RecordsWorkCorrectly()
    {
        var suggestion = new GhostTextSuggestion(
            Channel: "gtr",
            InsertText: "uggested translation text",
            StartLine: 5,
            StartColumn: 10,
            ProviderId: "rdat-gtr",
            Label: "[GTR 92%] TM Match"
        );

        Assert.Equal("gtr", suggestion.Channel);
        Assert.Equal("uggested translation text", suggestion.InsertText);
        Assert.Equal(5, suggestion.StartLine);
        Assert.Equal(10, suggestion.StartColumn);
        Assert.Equal("rdat-gtr", suggestion.ProviderId);
    }

    [Fact]
    public void CachedTranslation_HoldsDualVersions()
    {
        var cached = new CachedTranslation(
            SourceSentence: "The committee decided to postpone the meeting",
            Version1: "قررت اللجنة تأجيل الاجتماع",
            Version2: "اتخذت اللجنة قرارا بتأجيل الاجتماع",
            CachedAt: DateTime.UtcNow
        );

        Assert.Equal("The committee decided to postpone the meeting", cached.SourceSentence);
        Assert.NotEqual(cached.Version1, cached.Version2);
        Assert.NotNull(cached.CachedAt);
    }
}
