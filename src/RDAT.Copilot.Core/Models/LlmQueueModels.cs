namespace RDAT.Copilot.Core.Models;

/// <summary>
/// Priority level for LLM generation requests.
/// Higher priority preempts lower priority via CancellationToken.
/// </summary>
public enum LlmRequestPriority
{
    /// <summary>Background prefetch (Channel 3) — lowest priority. Generates dual translation versions.</summary>
    Prefetch = 0,

    /// <summary>Typing burst autocomplete (Channel 5) — medium-high priority. 3–5 words.</summary>
    Burst = 1,

    /// <summary>Typing pause continuation (Channel 6) — highest priority. 5–20 words.</summary>
    Pause = 2,

    /// <summary>Grammar check request — runs independently, not preempted.</summary>
    Grammar = 3,

    /// <summary>Rewrite request (Gemini cloud fallback) — user-initiated, non-preemptible.</summary>
    Rewrite = 4
}

/// <summary>
/// An LLM generation request in the priority queue.
/// Carries the prompt context, generation parameters, and a CancellationTokenSource
/// for priority-based preemption.
/// </summary>
public class LlmRequest
{
    /// <summary>Unique request identifier.</summary>
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Priority level — determines preemption behavior.</summary>
    public LlmRequestPriority Priority { get; init; }

    /// <summary>Channel name for logging and result routing ("prefetch", "burst", "pause", "grammar").</summary>
    public string Channel { get; init; } = "unknown";

    /// <summary>System prompt for the LLM.</summary>
    public string SystemPrompt { get; init; } = string.Empty;

    /// <summary>User message / context for generation.</summary>
    public string UserMessage { get; init; } = string.Empty;

    /// <summary>Maximum tokens to generate.</summary>
    public int MaxTokens { get; init; } = 200;

    /// <summary>Sampling temperature (0.0 = deterministic, 1.0 = creative).</summary>
    public float Temperature { get; init; } = 0.3f;

    /// <summary>Optional additional context (e.g., RAG match for augmentation).</summary>
    public string? Context { get; init; }

    /// <summary>Source sentence for tracking and prefetch caching.</summary>
    public string? SourceSentence { get; init; }

    /// <summary>Target line number for result routing.</summary>
    public int TargetLine { get; init; }

    /// <summary>Target column for result routing.</summary>
    public int TargetColumn { get; init; }

    /// <summary>Cancellation token source for priority preemption.</summary>
    public CancellationTokenSource CancellationTokenSource { get; } = new();

    /// <summary>UTC timestamp when the request was created.</summary>
    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    /// <summary>
    /// Build the full prompt including optional RAG context.
    /// </summary>
    public string BuildFullPrompt()
    {
        if (!string.IsNullOrEmpty(Context))
        {
            return $"{UserMessage}\n\n[Reference Translation]: {Context}";
        }

        return UserMessage;
    }
}

/// <summary>
/// Result of an LLM generation request.
/// </summary>
public record LlmGenerationResult(
    string RequestId,
    string Channel,
    LlmRequestPriority Priority,
    string? GeneratedText,
    bool Success,
    bool WasPreempted,
    bool WasCancelled,
    double GenerationMs,
    int TokensGenerated,
    string? ErrorMessage = null
);

/// <summary>
/// Channel-specific generation statistics for the status bar.
/// </summary>
public record ChannelStats(
    string Channel,
    int TotalRequests,
    int SuccessfulGenerations,
    int Preemptions,
    int Errors,
    double AverageMs,
    long LastGeneratedAtUnix
);
