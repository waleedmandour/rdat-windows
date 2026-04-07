namespace RDAT.Copilot.Core.Models;

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
