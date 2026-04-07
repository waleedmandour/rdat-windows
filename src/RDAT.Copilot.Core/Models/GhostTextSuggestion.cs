namespace RDAT.Copilot.Core.Models;

/// <summary>
/// Ghost text suggestion from one of the Tri-Channel generators.
/// </summary>
public record GhostTextSuggestion(
    string Channel,      // "prefetch" | "burst" | "pause"
    string InsertText,
    int StartLine,
    int StartColumn,
    string ProviderId,
    string Label
);

/// <summary>
/// Cache entry for the Predictive Prefetch Engine (Channel 3).
/// Stores dual translation versions for a source sentence.
/// </summary>
public record CachedTranslation(
    string SourceSentence,
    string Version1,       // Formal/Literal
    string Version2,       // Natural/Standard
    DateTime CachedAt
);

/// <summary>
/// WebLLM engine state.
/// </summary>
public enum LlmState
{
    Idle,
    Initializing,
    Ready,
    Generating,
    Error
}

/// <summary>
/// RAG pipeline state.
/// </summary>
public enum RagState
{
    Idle,
    LoadingModel,
    Indexing,
    Ready,
    Searching,
    Error
}

/// <summary>
/// Inference state for the editor event loop.
/// </summary>
public enum InferenceState
{
    Idle,
    Running,
    Aborted,
    Completed
}

/// <summary>
/// Suggestion mode — GTR (verified TM) vs Zero-Shot (unverified LLM).
/// </summary>
public enum SuggestionMode
{
    Gtr,
    ZeroShot,
    Pause
}
