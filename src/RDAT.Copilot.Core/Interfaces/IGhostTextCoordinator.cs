namespace RDAT.Copilot.Core.Interfaces;

using RDAT.Copilot.Core.Models;

/// <summary>
/// Contract for the Ghost Text Coordinator (Phase 3).
/// Orchestrates all four ghost text channels (GTR, Prefetch, Burst, Pause)
/// and routes LLM generation results to the appropriate Monaco editor commands.
///
/// Channel Architecture:
///   Channel 1: GTR (RAG TM match) — from RagPipelineService (Phase 2)
///   Channel 3: Prefetch — background, dual-version translation of source N+3
///   Channel 5: Burst — typing pause 0.8s, 3–5 words autocomplete
///   Channel 6: Pause — typing pause 1.2s, 5–20 words continuation
///
/// Preemption: Burst/Pause cancel Prefetch via ILlmQueueService.
/// </summary>
public interface IGhostTextCoordinator
{
    /// <summary>Whether the coordinator is active and processing events.</summary>
    bool IsActive { get; }

    /// <summary>
    /// Start the coordinator event loop.
    /// Listens for cursor/text changes and dispatches generation requests
    /// to the appropriate channel with the correct priority.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop the coordinator and cancel all pending channel requests.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Notify the coordinator that the target editor cursor has moved.
    /// Triggers Burst/Pause debounce timers and RAG lookup.
    /// </summary>
    /// <param name="lineNumber">1-based line number.</param>
    /// <param name="column">1-based column number.</param>
    Task OnTargetCursorChangedAsync(int lineNumber, int column);

    /// <summary>
    /// Notify the coordinator that the target editor text has changed.
    /// Resets channel timers and clears stale suggestions.
    /// </summary>
    /// <param name="fullText">Complete text content of the target editor.</param>
    Task OnTargetTextChangedAsync(string fullText);

    /// <summary>
    /// Notify the coordinator that the source editor text has changed.
    /// Triggers Prefetch channel for the next unmodified source sentence.
    /// </summary>
    /// <param name="fullText">Complete text content of the source editor.</param>
    Task OnSourceTextChangedAsync(string fullText);

    /// <summary>
    /// Raised when a ghost text suggestion is ready for display.
    /// Subscribers (WorkspacePage) push the suggestion to Monaco via WebViewBridge.
    /// </summary>
    event EventHandler<GhostTextSuggestion>? SuggestionReady;

    /// <summary>
    /// Raised when ghost text should be cleared (e.g., after acceptance or text change).
    /// </summary>
    event EventHandler<string>? ClearSuggestion;
}
