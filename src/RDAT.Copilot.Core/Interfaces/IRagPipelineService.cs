using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Core.Interfaces;

/// <summary>
/// Contract for the RAG (Retrieval-Augmented Generation) pipeline.
/// Orchestrates the flow: source sentence → embed → vector search → ranked results.
/// Provides context for ghost text generation and TM match display.
/// </summary>
public interface IRagPipelineService
{
    /// <summary>Current RAG pipeline state.</summary>
    RagState State { get; }

    /// <summary>Total number of TM entries indexed.</summary>
    long TotalTmCount { get; }

    /// <summary>Whether the pipeline is ready (embedding model loaded + DB open).</summary>
    bool IsReady { get; }

    /// <summary>
    /// Initialize the RAG pipeline: load embedding model and open vector database.
    /// </summary>
    /// <param name="modelPath">Path to the ONNX embedding model directory.</param>
    /// <param name="dbPath">Path to the LanceDB database directory.</param>
    /// <param name="progress">Progress reporter for model loading.</param>
    Task InitializeAsync(
        string modelPath,
        string dbPath,
        IProgress<(double Progress, string Text)>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Search for translation memory matches for the given source text.
    /// Returns ranked results with similarity scores.
    /// </summary>
    /// <param name="sourceText">Source-language text to search for.</param>
    /// <param name="topK">Maximum number of results (default: 5).</param>
    /// <param name="minimumScore">Minimum similarity score threshold (0.0-1.0).</param>
    /// <returns>Ranked TM search results.</returns>
    Task<IReadOnlyList<TmSearchResult>> SearchTmAsync(
        string sourceText,
        int topK = 5,
        double minimumScore = 0.5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Import a Translation Memory file: parse + embed + index.
    /// Combines <see cref="ITmImportService.ParseAsync"/> with embedding and indexing.
    /// </summary>
    /// <param name="filePath">Path to the TM file.</param>
    /// <param name="sourceLanguage">Source language code.</param>
    /// <param name="targetLanguage">Target language code.</param>
    /// <param name="domain">Optional domain tag.</param>
    /// <param name="progress">Progress reporter for import progress.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Import result with counts and errors.</returns>
    Task<TmImportResult> ImportTmFileAsync(
        string filePath,
        string sourceLanguage = "en",
        string targetLanguage = "ar",
        string? domain = null,
        IProgress<(int Imported, int Total, string Text)>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the best (highest-scoring) TM match for a source sentence.
    /// Convenience method for ghost text channel integration.
    /// </summary>
    /// <param name="sourceText">Source sentence text.</param>
    /// <returns>Best match, or null if no good match found.</returns>
    Task<TmSearchResult?> GetBestMatchAsync(string sourceText, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get statistics about the loaded Translation Memory.
    /// </summary>
    Task<TmStats> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Unload the embedding model and close the vector database.
    /// </summary>
    Task ShutdownAsync();
}
