namespace RDAT.Copilot.Core.Interfaces;

/// <summary>
/// Contract for the local embedding service (Phase 2).
/// Uses ONNX Runtime to run paraphrase-multilingual-MiniLM-L12-v2
/// for generating 384-dimensional text embeddings.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>Whether the embedding model is loaded.</summary>
    bool IsReady { get; }

    /// <summary>
    /// Initialize the embedding model from disk.
    /// </summary>
    Task InitializeAsync(string modelPath, IProgress<(double Progress, string Text)>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a 384-dimensional embedding vector for the given text.
    /// </summary>
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate embeddings for multiple texts in batch.
    /// </summary>
    Task<float[][]> EmbedBatchAsync(string[] texts, CancellationToken cancellationToken = default);
}
