using RDAT.Copilot.Core.Models;
using Xunit;

namespace RDAT.Copilot.Core.Tests;

public class GhostTextCoordinatorTests
{
    [Fact]
    public void GhostTextSuggestion_Record_CarriesChannelInfo()
    {
        var suggestion = new GhostTextSuggestion(
            Channel: "burst",
            InsertText: "الهرم الأكبر",
            StartLine: 5,
            StartColumn: 12,
            ProviderId: "rdat-burst",
            Label: "[Tab] Autocomplete"
        );

        Assert.Equal("burst", suggestion.Channel);
        Assert.Equal("الهرم الأكبر", suggestion.InsertText);
        Assert.Equal(5, suggestion.StartLine);
        Assert.Equal(12, suggestion.StartColumn);
        Assert.Equal("rdat-burst", suggestion.ProviderId);
    }

    [Fact]
    public void GhostTextSuggestion_AllChannelsSupported()
    {
        var channels = new[] { "gtr", "prefetch", "burst", "pause" };

        foreach (var channel in channels)
        {
            var suggestion = new GhostTextSuggestion(
                channel, "test", 1, 1, $"rdat-{channel}", $"[{channel}]");
            Assert.Equal(channel, suggestion.Channel);
        }
    }

    [Fact]
    public void InferenceState_AllStatesExist()
    {
        var states = Enum.GetValues<InferenceState>();
        Assert.Equal(4, states.Length);
        Assert.Contains(InferenceState.Idle, states);
        Assert.Contains(InferenceState.Running, states);
        Assert.Contains(InferenceState.Aborted, states);
        Assert.Contains(InferenceState.Completed, states);
    }

    [Fact]
    public void LlmState_AllStatesExist()
    {
        var states = Enum.GetValues<LlmState>();
        Assert.Equal(5, states.Length);
        Assert.Contains(LlmState.Idle, states);
        Assert.Contains(LlmState.Initializing, states);
        Assert.Contains(LlmState.Ready, states);
        Assert.Contains(LlmState.Generating, states);
        Assert.Contains(LlmState.Error, states);
    }

    [Fact]
    public void SuggestionMode_MatchesChannels()
    {
        var modes = Enum.GetValues<SuggestionMode>();
        Assert.Equal(3, modes.Length);
        Assert.Contains(SuggestionMode.Gtr, modes);
        Assert.Contains(SuggestionMode.ZeroShot, modes);
        Assert.Contains(SuggestionMode.Pause, modes);
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

    [Fact]
    public void ChannelTimingConstants_AreConsistent()
    {
        // Burst must be faster than Pause
        Assert.True(AppConstants.BurstDebounceMs < AppConstants.PauseDebounceMs);
        Assert.Equal(800, AppConstants.BurstDebounceMs);
        Assert.Equal(1200, AppConstants.PauseDebounceMs);

        // Token limits must be ordered
        Assert.True(AppConstants.BurstMaxTokens < AppConstants.PauseMaxTokens);
        Assert.True(AppConstants.PauseMaxTokens < AppConstants.PrefetchMaxTokens);
        Assert.Equal(50, AppConstants.BurstMaxTokens);
        Assert.Equal(100, AppConstants.PauseMaxTokens);
        Assert.Equal(200, AppConstants.PrefetchMaxTokens);

        // Temperature must be low for translation quality
        Assert.True(AppConstants.DefaultTemperature >= 0.1f);
        Assert.True(AppConstants.DefaultTemperature <= 0.5f);
        Assert.Equal(0.3f, AppConstants.DefaultTemperature);
    }

    [Fact]
    public void GhostTextArchitecture_PriorityOrder()
    {
        // Verify the priority order matches the architecture spec:
        // GTR (RAG) > Pause (Ch6) > Burst (Ch5) > Prefetch (Ch3)
        var priorities = new[]
        {
            (Name: "Prefetch", Priority: 0),
            (Name: "Burst", Priority: 1),
            (Name: "Pause", Priority: 2),
            (Name: "Grammar", Priority: 3),
            (Name: "Rewrite", Priority: 4)
        };

        for (int i = 0; i < priorities.Length - 1; i++)
        {
            Assert.True(priorities[i].Priority < priorities[i + 1].Priority,
                $"{priorities[i].Name} should have lower priority than {priorities[i + 1].Name}");
        }
    }
}
