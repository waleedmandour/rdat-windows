using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Core.Interfaces;

/// <summary>
/// Contract for the disk-backed vector database (Phase 2).
/// Uses LanceDB or SQLite-vec for persistent storage of
/// 10M+ sentence Translation Memories.
/// </summary>
public interface IVectorDatabaseService
{
    /// <summary>Whether the database is ready for queries.</summary>
    bool IsReady { get; }

    /// <summary>
    /// Open or create the database at the given path.
    /// </summary>
    Task OpenAsync(string dbPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Index a collection of text entries with their embeddings.
    /// </summary>
    Task IndexBatchAsync(IEnumerable<(string Id, string SourceText, string TargetText, float[] Embedding)> entries, CancellationToken cancellationToken = default);

    /// <summary>
    /// Search for the top-K most similar entries to the query embedding.
    /// </summary>
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] queryEmbedding, int topK = 5, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the total number of indexed entries.
    /// </summary>
    Task<long> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Close the database connection.
    /// </summary>
    Task CloseAsync(CancellationToken cancellationToken = default);
}
