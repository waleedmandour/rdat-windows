using RDAT.Copilot.Core.Models;

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
