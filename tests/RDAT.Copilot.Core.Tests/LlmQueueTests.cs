using RDAT.Copilot.Core.Models;
using Xunit;

namespace RDAT.Copilot.Core.Tests;

public class LlmQueueTests
{
    [Fact]
    public void LlmRequest_CreatesWithDefaults()
    {
        var request = new LlmRequest
        {
            Channel = "burst",
            SystemPrompt = "You are a translator.",
            UserMessage = "Translate this sentence.",
            MaxTokens = 50,
            Temperature = 0.3f,
            Priority = LlmRequestPriority.Burst
        };

        Assert.Equal("burst", request.Channel);
        Assert.Equal(50, request.MaxTokens);
        Assert.Equal(0.3f, request.Temperature);
        Assert.Equal(LlmRequestPriority.Burst, request.Priority);
        Assert.NotNull(request.CancellationTokenSource);
        Assert.NotEmpty(request.Id);
        Assert.True(request.CreatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void LlmRequest_BuildFullPrompt_WithContext()
    {
        var request = new LlmRequest
        {
            UserMessage = "Translate the following text.",
            Context = "مرحبا بالعالم"
        };

        var fullPrompt = request.BuildFullPrompt();
        Assert.Contains("Translate the following text.", fullPrompt);
        Assert.Contains("مرحبا بالعالم", fullPrompt);
    }

    [Fact]
    public void LlmRequest_BuildFullPrompt_WithoutContext()
    {
        var request = new LlmRequest
        {
            UserMessage = "Translate the following text."
        };

        var fullPrompt = request.BuildFullPrompt();
        Assert.Equal("Translate the following text.", fullPrompt);
    }

    [Fact]
    public void LlmRequest_PriorityOrder_IsCorrect()
    {
        Assert.True((int)LlmRequestPriority.Prefetch < (int)LlmRequestPriority.Burst);
        Assert.True((int)LlmRequestPriority.Burst < (int)LlmRequestPriority.Pause);
        Assert.True((int)LlmRequestPriority.Pause < (int)LlmRequestPriority.Grammar);
        Assert.True((int)LlmRequestPriority.Grammar < (int)LlmRequestPriority.Rewrite);
    }

    [Fact]
    public void LlmGenerationResult_SuccessState()
    {
        var result = new LlmGenerationResult(
            RequestId: "abc123",
            Channel: "burst",
            Priority: LlmRequestPriority.Burst,
            GeneratedText: "كلمة",
            Success: true,
            WasPreempted: false,
            WasCancelled: false,
            GenerationMs: 150.5,
            TokensGenerated: 5
        );

        Assert.True(result.Success);
        Assert.False(result.WasPreempted);
        Assert.Equal("abc123", result.RequestId);
        Assert.Equal("burst", result.Channel);
        Assert.Equal(150.5, result.GenerationMs);
        Assert.Equal(5, result.TokensGenerated);
    }

    [Fact]
    public void LlmGenerationResult_PreemptedState()
    {
        var result = new LlmGenerationResult(
            RequestId: "def456",
            Channel: "prefetch",
            Priority: LlmRequestPriority.Prefetch,
            GeneratedText: null,
            Success: false,
            WasPreempted: true,
            WasCancelled: true,
            GenerationMs: 50.0,
            TokensGenerated: 0
        );

        Assert.False(result.Success);
        Assert.True(result.WasPreempted);
        Assert.True(result.WasCancelled);
        Assert.Null(result.GeneratedText);
    }

    [Fact]
    public void LlmGenerationResult_ErrorState()
    {
        var result = new LlmGenerationResult(
            RequestId: "ghi789",
            Channel: "burst",
            Priority: LlmRequestPriority.Burst,
            GeneratedText: null,
            Success: false,
            WasPreempted: false,
            WasCancelled: false,
            GenerationMs: 10.0,
            TokensGenerated: 0,
            ErrorMessage: "Model not loaded"
        );

        Assert.False(result.Success);
        Assert.False(result.WasPreempted);
        Assert.Equal("Model not loaded", result.ErrorMessage);
    }

    [Fact]
    public void ChannelStats_TracksCorrectly()
    {
        var stats = new ChannelStats(
            Channel: "burst",
            TotalRequests: 100,
            SuccessfulGenerations: 92,
            Preemptions: 5,
            Errors: 3,
            AverageMs: 120.5,
            LastGeneratedAtUnix: DateTime.UtcNow.Ticks
        );

        Assert.Equal(100, stats.TotalRequests);
        Assert.Equal(92, stats.SuccessfulGenerations);
        Assert.Equal(5, stats.Preemptions);
        Assert.Equal(3, stats.Errors);
        Assert.Equal(120.5, stats.AverageMs);
    }

    [Theory]
    [InlineData(LlmRequestPriority.Prefetch, 0)]
    [InlineData(LlmRequestPriority.Burst, 1)]
    [InlineData(LlmRequestPriority.Pause, 2)]
    [InlineData(LlmRequestPriority.Grammar, 3)]
    [InlineData(LlmRequestPriority.Rewrite, 4)]
    public void LlmRequestPriority_HasCorrectIntValue(LlmRequestPriority priority, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)priority);
    }
}
