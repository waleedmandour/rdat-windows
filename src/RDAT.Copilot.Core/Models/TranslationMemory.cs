namespace RDAT.Copilot.Core.Models;

/// <summary>
/// A Translation Memory entry representing a source-target translation pair.
/// Stored as a row in LanceDB with an associated embedding vector.
/// </summary>
/// <param name="Id">Unique identifier (GUID or import-provided ID).</param>
/// <param name="SourceText">Original source-language sentence.</param>
/// <param name="TargetText">Translated target-language sentence.</param>
/// <param name="SourceLanguage">ISO 639-1 source language code (e.g., "en").</param>
/// <param name="TargetLanguage">ISO 639-1 target language code (e.g., "ar").</param>
/// <param name="Domain">Optional domain/category tag (e.g., "Legal", "Medical").</param>
/// <param name="QualityScore">Confidence score from 0.0 to 1.0 (1.0 = verified).</param>
/// <param name="CreatedAt">UTC timestamp when this entry was imported/created.</param>
public record TmEntry(
    string Id,
    string SourceText,
    string TargetText,
    string SourceLanguage = "en",
    string TargetLanguage = "ar",
    string? Domain = null,
    double QualityScore = 1.0,
    DateTime? CreatedAt = null
);

/// <summary>
/// Result of a Translation Memory search (vector similarity lookup).
/// Wraps the raw LanceDB hit with display metadata.
/// </summary>
/// <param name="Entry">The matched TM entry.</param>
/// <param name="Score">Cosine similarity score (0.0 to 1.0). Higher = better match.</param>
/// <param name="SearchMs">Time taken for the vector search in milliseconds.</param>
public record TmSearchResult(
    TmEntry Entry,
    double Score,
    double SearchMs
);

/// <summary>
/// Statistics for a Translation Memory database.
/// </summary>
/// <param name="TotalEntries">Total number of indexed TM pairs.</param>
/// <param name="DomainBreakdown">Count per domain.</param>
/// <param name="LanguagePairs">Source→Target language pair counts.</param>
/// <param name="DbSizeMb">Database file size on disk in megabytes.</param>
public record TmStats(
    long TotalEntries,
    IReadOnlyDictionary<string, int> DomainBreakdown,
    IReadOnlyDictionary<string, int> LanguagePairs,
    double DbSizeMb
);

/// <summary>
/// A vector search result with similarity score.
/// </summary>
public record VectorSearchResult(
    string Id,
    string SourceText,
    string TargetText,
    float Score,
    double SearchMilliseconds
);

/// <summary>
/// Result of a TM import operation (TMX or CSV).
/// </summary>
/// <param name="TotalRows">Total rows found in the import file.</param>
/// <param name="ImportedCount">Number of successfully imported rows.</param>
/// <param name="SkippedCount">Number of skipped rows (duplicates, empty).</param>
/// <param name="ErrorCount">Number of rows that failed to import.</param>
/// <param name="Errors">List of error messages for failed rows.</param>
/// <param name="ElapsedMs">Total import time in milliseconds.</param>
public record TmImportResult(
    int TotalRows,
    int ImportedCount,
    int SkippedCount,
    int ErrorCount,
    IReadOnlyList<string> Errors,
    double ElapsedMs
);

/// <summary>
/// Supported TM import file formats.
/// </summary>
public enum TmImportFormat
{
    /// <summary>CSV with columns: source, target (and optional: domain, quality).</summary>
    Csv,

    /// <summary>Translation Memory eXchange (TMX 1.4) XML format.</summary>
    Tmx,

    /// <summary>Tab-separated text file with source\ttarget per line.</summary>
    Tsv
}

/// <summary>
/// LanceDB table row schema for TM storage.
/// Uses simple serializable types that LanceDB can store natively.
/// </summary>
public record LanceTmRow
{
    public string Id { get; init; } = string.Empty;
    public string SourceText { get; init; } = string.Empty;
    public string TargetText { get; init; } = string.Empty;
    public string SourceLanguage { get; init; } = "en";
    public string TargetLanguage { get; init; } = "ar";
    public string? Domain { get; init; }
    public double QualityScore { get; init; } = 1.0;
    public long CreatedAtUnix { get; init; }

    /// <summary>
    /// Converts a <see cref="TmEntry"/> to a <see cref="LanceTmRow"/> for storage.
    /// </summary>
    public static LanceTmRow FromTmEntry(TmEntry entry)
    {
        return new LanceTmRow
        {
            Id = entry.Id,
            SourceText = entry.SourceText,
            TargetText = entry.TargetText,
            SourceLanguage = entry.SourceLanguage,
            TargetLanguage = entry.TargetLanguage,
            Domain = entry.Domain,
            QualityScore = entry.QualityScore,
            CreatedAtUnix = new DateTimeOffset(entry.CreatedAt ?? DateTime.UtcNow).ToUnixTimeSeconds()
        };
    }

    /// <summary>
    /// Converts back to a <see cref="TmEntry"/>.
    /// </summary>
    public TmEntry ToTmEntry()
    {
        return new TmEntry(
            Id,
            SourceText,
            TargetText,
            SourceLanguage,
            TargetLanguage,
            Domain,
            QualityScore,
            DateTimeOffset.FromUnixTimeSeconds(CreatedAtUnix).DateTime
        );
    }
}
