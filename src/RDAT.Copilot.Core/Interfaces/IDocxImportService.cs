namespace RDAT.Copilot.Core.Interfaces;

/// <summary>
/// Contract for importing .docx files and extracting paragraph-level content.
/// Supports bilingual documents with paragraph-by-paragraph alignment.
/// </summary>
public interface IDocxImportService
{
    /// <summary>
    /// Import a .docx file and extract all paragraphs.
    /// Returns a list of document segments with paragraph text and metadata.
    /// </summary>
    Task<DocxImportResult> ImportAsync(
        string filePath,
        IProgress<(double Progress, string Text)>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a .docx import operation.
/// </summary>
public record DocxImportResult(
    string FilePath,
    string FileName,
    int TotalParagraphs,
    IReadOnlyList<DocxSegment> Segments,
    string? Error = null
);

/// <summary>
/// A single segment from a .docx document (typically one paragraph).
/// </summary>
public record DocxSegment(
    int Index,
    string Text,
    string Style,
    bool IsEmpty,
    int StartLineNumber,
    int EndLineNumber
);
