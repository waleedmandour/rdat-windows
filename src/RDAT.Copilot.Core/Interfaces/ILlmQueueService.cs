using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Core.Interfaces;

/// <summary>
/// Contract for the LLM priority queue engine (Phase 3).
/// Implements a single-consumer, multi-producer channel that processes
/// generation requests by priority, with CancellationToken-based preemption.
///
/// Preemption rules:
///   - Burst (priority 1) cancels any running Prefetch (priority 0)
///   - Pause (priority 2) cancels any running Prefetch or Burst
///   - Grammar/Rewrite requests are never preempted
///
/// The queue ensures exactly one generation runs at a time on the GPU,
/// maximizing DirectML throughput.
/// </summary>
public interface ILlmQueueService
{
    /// <summary>Whether the queue is running.</summary>
    bool IsRunning { get; }

    /// <summary>Current number of pending requests in the queue.</summary>
    int PendingCount { get; }

    /// <summary>Generation statistics per channel.</summary>
    IReadOnlyDictionary<string, ChannelStats> ChannelStatistics { get; }

    /// <summary>
    /// Start the queue processing loop.
    /// The queue reads from a priority channel and dispatches to the inference service.
    /// </summary>
    /// <param name="cancellationToken">Token to stop the queue loop.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop the queue processing loop gracefully.
    /// Cancels all pending requests and waits for the current generation to finish.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueue a generation request with the specified priority.
    /// If a lower-priority request is currently running, it will be preempted.
    /// </summary>
    /// <param name="request">The LLM generation request.</param>
    /// <returns>The unique request ID for tracking.</returns>
    Task<string> EnqueueAsync(LlmRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancel all pending requests in the queue.
    /// Does NOT cancel the currently running request.
    /// </summary>
    void ClearPending();

    /// <summary>
    /// Raised when a generation result is produced.
    /// Subscribers receive the result to route to the appropriate channel.
    /// </summary>
    event EventHandler<LlmGenerationResult>? GenerationCompleted;
}
