using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Core.Interfaces;

/// <summary>
/// Contract for importing Translation Memory files (TMX, CSV, TSV).
/// Parses file contents into <see cref="TmEntry"/> records for indexing.
/// </summary>
public interface ITmImportService
{
    /// <summary>
    /// Detects the file format from the file extension and content.
    /// </summary>
    /// <param name="filePath">Path to the TM file.</param>
    /// <returns>The detected import format.</returns>
    TmImportFormat DetectFormat(string filePath);

    /// <summary>
    /// Reads a Translation Memory file and parses it into TmEntry records.
    /// Does not index the entries — that is handled by <see cref="IVectorDatabaseService.IndexBatchAsync"/>.
    /// </summary>
    /// <param name="filePath">Path to the TM file (.csv, .tmx, .tsv).</param>
    /// <param name="sourceLanguage">Source language code (default: "en").</param>
    /// <param name="targetLanguage">Target language code (default: "ar").</param>
    /// <param name="domain">Optional domain tag to apply to all entries.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Parsed TM entries.</returns>
    Task<IReadOnlyList<TmEntry>> ParseAsync(
        string filePath,
        string sourceLanguage = "en",
        string targetLanguage = "ar",
        string? domain = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimates the number of translation units in a file without fully parsing it.
    /// Used for progress reporting before starting a large import.
    /// </summary>
    /// <param name="filePath">Path to the TM file.</param>
    /// <returns>Estimated number of translation units.</returns>
    Task<int> EstimateCountAsync(string filePath);
}
