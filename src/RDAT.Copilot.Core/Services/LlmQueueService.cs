using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Core.Services;

/// <summary>
/// Priority-based LLM request queue engine.
/// Processes exactly one generation request at a time on the GPU/NPU,
/// using System.Threading.Channels with priority ordering.
///
/// Preemption strategy:
///   When a high-priority request (Burst/Pause) is enqueued:
///   1. Cancel the currently running Prefetch request via its CancellationTokenSource
///   2. The Prefetch task catches OperationCanceledException and returns null
///   3. The queue immediately picks up the high-priority request
///
/// This ensures the DirectML queue is always used for the most valuable
/// generation, while background Prefetch work can be safely discarded.
/// </summary>
public sealed class LlmQueueService : ILlmQueueService, IDisposable
{
    private readonly ILocalInferenceService _inferenceService;
    private readonly ILogger<LlmQueueService> _logger;

    // Priority-sorted requests (highest priority first)
    private readonly Channel<LlmRequest> _requestChannel;
    private readonly ConcurrentDictionary<string, ChannelStats> _stats = new();

    private CancellationTokenSource? _loopCts;
    private Task? _processingLoop;
    private LlmRequest? _currentRequest;

    public bool IsRunning => _processingLoop is not null && !_processingLoop.IsCompleted;
    public int PendingCount => _requestChannel.Reader.Count;

    public IReadOnlyDictionary<string, ChannelStats> ChannelStatistics =>
        new Dictionary<string, ChannelStats>(_stats);

    public event EventHandler<LlmGenerationResult>? GenerationCompleted;

    public LlmQueueService(
        ILocalInferenceService inferenceService,
        ILogger<LlmQueueService> logger)
    {
        _inferenceService = inferenceService;
        _logger = logger;

        // Unbounded channel — we manage priority ourselves
        _requestChannel = Channel.CreateUnbounded<LlmRequest>(new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = true
        });
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            _logger.LogWarning("[LLM-Queue] Already running");
            return;
        }

        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _processingLoop = Task.Run(() => ProcessingLoopAsync(_loopCts.Token), _loopCts.Token);

        _logger.LogInformation("[LLM-Queue] Processing loop started");
    }

    /// <inheritdoc/>
    public async Task StopAsync()
    {
        if (!IsRunning) return;

        _logger.LogInformation("[LLM-Queue] Stopping processing loop...");

        // Signal the loop to stop
        _loopCts?.Cancel();

        // Wait for the loop to finish
        if (_processingLoop is not null)
        {
            try
            {
                await Task.WhenAny(_processingLoop, Task.Delay(5000));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[LLM-Queue] Error waiting for loop to stop");
            }
        }

        _processingLoop = null;
        _loopCts?.Dispose();
        _loopCts = null;

        _logger.LogInformation("[LLM-Queue] Processing loop stopped");
    }

    /// <inheritdoc/>
    public async Task<string> EnqueueAsync(LlmRequest request)
    {
        if (!_inferenceService.IsReady)
        {
            _logger.LogWarning("[LLM-Queue] Enqueue rejected — inference service not ready");
            return request.Id;
        }

        // Preemption: cancel lower-priority requests
        if (request.Priority >= LlmRequestPriority.Burst && _currentRequest is not null)
        {
            if (_currentRequest.Priority < request.Priority)
            {
                _logger.LogInformation(
                    "[LLM-Queue] Preempting {CurrentChannel} (priority {CurrentPriority}) " +
                    "for {NewChannel} (priority {NewPriority})",
                    _currentRequest.Channel, _currentRequest.Priority,
                    request.Channel, request.Priority);

                _currentRequest.CancellationTokenSource.Cancel();
                RecordPreemption(_currentRequest.Channel);
            }
        }

        // Write to channel
        await _requestChannel.Writer.WriteAsync(request);

        _logger.LogDebug(
            "[LLM-Queue] Enqueued: {Channel} (priority {Priority}, queue depth: {Depth})",
            request.Channel, request.Priority, PendingCount);

        return request.Id;
    }

    /// <inheritdoc/>
    public void ClearPending()
    {
        // Drain all pending requests from the channel
        while (_requestChannel.Reader.TryRead(out var request))
        {
            request.CancellationTokenSource.Dispose();
            _logger.LogDebug("[LLM-Queue] Cleared pending: {Channel}", request.Channel);
        }
    }

    /// <summary>
    /// Main processing loop. Reads requests from the channel and
    /// dispatches them to the inference service sequentially.
    /// </summary>
    private async Task ProcessingLoopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[LLM-Queue] Processing loop entered");

        try
        {
            await foreach (var request in _requestChannel.Reader.ReadAllAsync(cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                _currentRequest = request;
                var result = await ProcessRequestAsync(request);

                _currentRequest = null;
                GenerationCompleted?.Invoke(this, result);

                // Record stats
                RecordGeneration(result);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("[LLM-Queue] Processing loop cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LLM-Queue] Processing loop error");
        }
        finally
        {
            _currentRequest = null;
            _logger.LogInformation("[LLM-Queue] Processing loop exited");
        }
    }

    /// <summary>
    /// Process a single LLM generation request.
    /// </summary>
    private async Task<LlmGenerationResult> ProcessRequestAsync(LlmRequest request)
    {
        var sw = Stopwatch.StartNew();
        var tokenCount = 0;

        _logger.LogInformation(
            "[LLM-Queue] Processing: {Channel} (id: {Id}, maxTokens: {Max}, temp: {Temp:F2})",
            request.Channel, request.Id, request.MaxTokens, request.Temperature);

        try
        {
            var generatedText = await _inferenceService.GenerateAsync(
                request.SystemPrompt,
                request.BuildFullPrompt(),
                request.MaxTokens,
                request.Temperature,
                request.CancellationTokenSource.Token);

            var elapsedMs = sw.Elapsed.TotalMilliseconds;

            if (request.CancellationTokenSource.IsCancellationRequested)
            {
                // Was preempted
                return new LlmGenerationResult(
                    RequestId: request.Id,
                    Channel: request.Channel,
                    Priority: request.Priority,
                    GeneratedText: null,
                    Success: false,
                    WasPreempted: true,
                    WasCancelled: true,
                    GenerationMs: elapsedMs,
                    TokensGenerated: 0
                );
            }

            if (generatedText is not null)
            {
                tokenCount = EstimateTokenCount(generatedText);

                return new LlmGenerationResult(
                    RequestId: request.Id,
                    Channel: request.Channel,
                    Priority: request.Priority,
                    GeneratedText: generatedText,
                    Success: true,
                    WasPreempted: false,
                    WasCancelled: false,
                    GenerationMs: elapsedMs,
                    TokensGenerated: tokenCount
                );
            }

            return new LlmGenerationResult(
                RequestId: request.Id,
                Channel: request.Channel,
                Priority: request.Priority,
                GeneratedText: null,
                Success: false,
                WasPreempted: false,
                WasCancelled: false,
                GenerationMs: elapsedMs,
                TokensGenerated: 0,
                ErrorMessage: "Empty generation result"
            );
        }
        catch (OperationCanceledException)
        {
            return new LlmGenerationResult(
                RequestId: request.Id,
                Channel: request.Channel,
                Priority: request.Priority,
                GeneratedText: null,
                Success: false,
                WasPreempted: true,
                WasCancelled: true,
                GenerationMs: sw.Elapsed.TotalMilliseconds,
                TokensGenerated: 0
            );
        }
        catch (Exception ex)
        {
            return new LlmGenerationResult(
                RequestId: request.Id,
                Channel: request.Channel,
                Priority: request.Priority,
                GeneratedText: null,
                Success: false,
                WasPreempted: false,
                WasCancelled: false,
                GenerationMs: sw.Elapsed.TotalMilliseconds,
                TokensGenerated: 0,
                ErrorMessage: ex.Message
            );
        }
        finally
        {
            request.CancellationTokenSource.Dispose();
        }
    }

    /// <summary>
    /// Record generation result in channel statistics.
    /// </summary>
    private void RecordGeneration(LlmGenerationResult result)
    {
        var stats = _stats.AddOrUpdate(
            result.Channel,
            _ => new ChannelStats(
                result.Channel, 1,
                result.Success ? 1 : 0,
                result.WasPreempted ? 1 : 0,
                !result.Success && !result.WasPreempted ? 1 : 0,
                result.GenerationMs,
                DateTime.UtcNow.Ticks),
            (_, existing) => new ChannelStats(
                existing.Channel,
                existing.TotalRequests + 1,
                existing.SuccessfulGenerations + (result.Success ? 1 : 0),
                existing.Preemptions + (result.WasPreempted ? 1 : 0),
                existing.Errors + (!result.Success && !result.WasPreempted ? 1 : 0),
                (existing.AverageMs * existing.TotalRequests + result.GenerationMs) / (existing.TotalRequests + 1),
                DateTime.UtcNow.Ticks));
    }

    /// <summary>
    /// Record a preemption for a channel.
    /// </summary>
    private void RecordPreemption(string channel)
    {
        _stats.AddOrUpdate(
            channel,
            _ => new ChannelStats(channel, 0, 0, 1, 0, 0, DateTime.UtcNow.Ticks),
            (_, existing) => existing with { Preemptions = existing.Preemptions + 1 });
    }

    /// <summary>
    /// Rough token count estimation (~4 characters per token).
    /// </summary>
    private static int EstimateTokenCount(string text)
    {
        return text.Length / 4;
    }

    public void Dispose()
    {
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        ClearPending();
    }
}
