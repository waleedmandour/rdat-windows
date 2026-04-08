using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Core.Services;

/// <summary>
/// Imports .docx files using DocumentFormat.OpenXml and extracts paragraph-level
/// content as segments for the translation workspace.
///
/// Each paragraph in the document becomes a segment. Empty paragraphs are preserved
/// for paragraph-by-paragraph alignment between source and target editors.
/// Handles Arabic text (RTL) and various paragraph styles (Heading1, Normal, etc.).
/// </summary>
public sealed class DocxImportService : IDocxImportService
{
    private readonly ILogger<DocxImportService> _logger;

    public DocxImportService(ILogger<DocxImportService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<DocxImportResult> ImportAsync(
        string filePath,
        IProgress<(double Progress, string Text)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[RDAT] Starting .docx import: {FilePath}", filePath);

        if (!File.Exists(filePath))
        {
            var msg = $"File not found: {filePath}";
            _logger.LogError("[RDAT] {Message}", msg);
            return new DocxImportResult(filePath, Path.GetFileName(filePath), 0,
                Array.Empty<DocxSegment>(), Error: msg);
        }

        if (!filePath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            var msg = $"Unsupported file format: {Path.GetExtension(filePath)}. Only .docx files are supported.";
            _logger.LogError("[RDAT] {Message}", msg);
            return new DocxImportResult(filePath, Path.GetFileName(filePath), 0,
                Array.Empty<DocxSegment>(), Error: msg);
        }

        try
        {
            var result = await Task.Run(() =>
            {
                var segments = new List<DocxSegment>();
                var fileName = Path.GetFileName(filePath);

                // Open the .docx file in read-only mode
                using var document = WordprocessingDocument.Open(filePath, false);
                if (document.MainDocumentPart?.Document.Body is null)
                {
                    _logger.LogWarning("[RDAT] Document has no body content: {FilePath}", filePath);
                    return new DocxImportResult(filePath, fileName, 0,
                        segments, Error: "Document has no body content.");
                }

                var body = document.MainDocumentPart.Document.Body;
                var paragraphs = body.Elements<Paragraph>().ToList();
                var totalParagraphs = paragraphs.Count;

                _logger.LogInformation("[RDAT] Document has {Count} paragraphs: {FilePath}",
                    totalParagraphs, filePath);

                progress?.Report((0.0, $"Reading {totalParagraphs} paragraphs..."));

                var lineNumber = 1;

                for (var i = 0; i < totalParagraphs; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var paragraph = paragraphs[i];
                    var text = GetParagraphText(paragraph);
                    var style = GetParagraphStyle(paragraph);
                    var isEmpty = string.IsNullOrWhiteSpace(text);

                    // Track line range for this segment
                    // Empty paragraphs still occupy one line for alignment
                    var startLine = lineNumber;
                    var endLine = lineNumber;

                    // Multi-line paragraphs span multiple lines
                    if (!isEmpty)
                    {
                        // Account for internal newlines within a paragraph
                        var lineCount = text.Count(c => c == '\n') + 1;
                        endLine = lineNumber + lineCount - 1;
                    }

                    segments.Add(new DocxSegment(
                        Index: i,
                        Text: text ?? string.Empty,
                        Style: style,
                        IsEmpty: isEmpty,
                        StartLineNumber: startLine,
                        EndLineNumber: endLine
                    ));

                    lineNumber = endLine + 1;

                    // Report progress periodically
                    if (i % 50 == 0 || i == totalParagraphs - 1)
                    {
                        var percent = (double)(i + 1) / totalParagraphs * 100;
                        progress?.Report((percent, $"Importing paragraph {i + 1}/{totalParagraphs}..."));
                    }
                }

                _logger.LogInformation("[RDAT] .docx import complete: {Count} segments from {FilePath}",
                    segments.Count, filePath);

                return new DocxImportResult(filePath, fileName, totalParagraphs, segments);
            }, cancellationToken).ConfigureAwait(false);

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[RDAT] .docx import cancelled: {FilePath}", filePath);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RDAT] .docx import failed: {FilePath}", filePath);
            return new DocxImportResult(filePath, Path.GetFileName(filePath), 0,
                Array.Empty<DocxSegment>(), Error: ex.Message);
        }
    }

    /// <summary>
    /// Extracts the text content from a paragraph by concatenating all text runs.
    /// Handles tab characters, breaks, and preserves Arabic/RTL text.
    /// </summary>
    private static string GetParagraphText(Paragraph paragraph)
    {
        var textBuilder = new System.Text.StringBuilder();

        foreach (var run in paragraph.Elements<Run>())
        {
            // Concatenate text from each <w:t> element in the run
            foreach (var textElement in run.Elements<Text>())
            {
                textBuilder.Append(textElement.Text);
            }

            // Handle tab characters
            foreach (var tabChar in run.Elements<TabChar>())
            {
                textBuilder.Append('\t');
            }

            // Handle break characters (line breaks within a paragraph)
            foreach (var br in run.Elements<Break>())
            {
                textBuilder.Append('\n');
            }
        }

        return textBuilder.ToString();
    }

    /// <summary>
    /// Extracts the paragraph style name from the paragraph properties.
    /// Returns "Normal" if no style is specified, or the raw style ID otherwise.
    /// Maps common style IDs to readable names (Heading1, Heading2, etc.).
    /// </summary>
    private static string GetParagraphStyle(Paragraph paragraph)
    {
        var paragraphProperties = paragraph.ParagraphProperties;
        if (paragraphProperties?.ParagraphStyleId is null)
        {
            return "Normal";
        }

        var styleId = paragraphProperties.ParagraphStyleId.Val?.Value;
        if (string.IsNullOrEmpty(styleId))
        {
            return "Normal";
        }

        // Map common Word style IDs to readable names
        return styleId.ToUpperInvariant() switch
        {
            "HEADING1" or "HEADING 1" => "Heading1",
            "HEADING2" or "HEADING 2" => "Heading2",
            "HEADING3" or "HEADING 3" => "Heading3",
            "HEADING4" or "HEADING 4" => "Heading4",
            "TITLE" => "Title",
            "SUBTITLE" => "Subtitle",
            "LISTPARAGRAPH" => "ListParagraph",
            "QUOTE" => "Quote",
            "TOC.HEADING" => "TOCHeading",
            _ => styleId
        };
    }
}
