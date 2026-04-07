using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Core.Interfaces;

/// <summary>
/// Contract for the local LLM inference engine (Phase 3).
/// Uses OnnxRuntimeGenAI with DirectML for GPU/NPU execution.
/// Implements queue-based request processing with priority preemption.
/// </summary>
public interface ILocalInferenceService
{
    /// <summary>Current engine state.</summary>
    LlmState State { get; }

    /// <summary>Whether the engine is ready to generate.</summary>
    bool IsReady { get; }

    /// <summary>
    /// Initialize the ONNX model from disk.
    /// </summary>
    Task InitializeAsync(string modelPath, IProgress<(double Progress, string Text)>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a completion from the chat messages.
    /// Returns null if the engine is not ready.
    /// </summary>
    Task<string?> GenerateAsync(
        string systemPrompt,
        string userMessage,
        int maxTokens = 200,
        float temperature = 0.3f,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Interrupt the current generation immediately.
    /// Used for priority preemption (burst/pause preempting prefetch).
    /// </summary>
    void InterruptGenerate();

    /// <summary>
    /// Unload the model and free GPU resources.
    /// </summary>
    Task UnloadAsync();
}
