using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Services;
using Xunit;

namespace RDAT.Copilot.Core.Tests;

/// <summary>
/// Tests for DocxImportService — Phase 5.
/// Creates minimal valid .docx files in memory for testing paragraph extraction,
/// style detection, Arabic text preservation, and cancellation support.
/// </summary>
public class DocxImportTests : IDisposable
{
    private readonly DocxImportService _service;
    private readonly string _testDir;

    public DocxImportTests()
    {
        // Create a real logger for diagnostic output
        var serviceProvider = new ServiceCollection()
            .AddLogging(builder => builder.AddDebug())
            .BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<DocxImportService>>();

        _service = new DocxImportService(logger);
        _testDir = Path.Combine(Path.GetTempPath(), $"RDAT_DocxTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch { }
    }

    /// <summary>
    /// Creates a minimal valid .docx file with the specified paragraphs.
    /// Each string in the array becomes a separate paragraph.
    /// </summary>
    private static string CreateDocxWithParagraphs(string dir, string fileName, string[] paragraphs)
    {
        var filePath = Path.Combine(dir, fileName);
        using var document = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);

        var mainPart = document.AddMainDocumentPart();
        var body = new Body();

        foreach (var text in paragraphs)
        {
            var para = new Paragraph();
            var run = new Run(new Text(text));
            para.AppendChild(run);
            body.AppendChild(para);
        }

        mainPart.Document = new Document(body);
        document.Save();
        document.Dispose();

        return filePath;
    }

    /// <summary>
    /// Creates a .docx file with styled paragraphs.
    /// </summary>
    private static string CreateDocxWithStyles(string dir, string fileName)
    {
        var filePath = Path.Combine(dir, fileName);
        using var document = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);

        var mainPart = document.AddMainDocumentPart();
        var body = new Body();

        // Normal paragraph
        var normalPara = new Paragraph();
        normalPara.AppendChild(new Run(new Text("This is a normal paragraph.")));
        body.AppendChild(normalPara);

        // Heading1 paragraph
        var heading1Para = new Paragraph();
        var heading1Props = new ParagraphProperties();
        heading1Props.ParagraphStyleId = new ParagraphStyleId { Val = "Heading1" };
        heading1Para.AppendChild(heading1Props);
        heading1Para.AppendChild(new Run(new Text("Chapter One")));
        body.AppendChild(heading1Para);

        // Another normal paragraph
        var normalPara2 = new Paragraph();
        normalPara2.AppendChild(new Run(new Text("Body text under the heading.")));
        body.AppendChild(normalPara2);

        // Empty paragraph
        var emptyPara = new Paragraph();
        body.AppendChild(emptyPara);

        // Heading2 paragraph
        var heading2Para = new Paragraph();
        var heading2Props = new ParagraphProperties();
        heading2Props.ParagraphStyleId = new ParagraphStyleId { Val = "Heading2" };
        heading2Para.AppendChild(heading2Props);
        heading2Para.AppendChild(new Run(new Text("Section 1.1")));
        body.AppendChild(heading2Para);

        mainPart.Document = new Document(body);
        document.Save();
        document.Dispose();

        return filePath;
    }

    // ─── Unit Tests ──────────────────────────────────────────────

    [Fact]
    public async Task ImportAsync_EmptyDocument_ReturnsZeroSegments()
    {
        // Create an empty .docx (no paragraphs)
        var filePath = Path.Combine(_testDir, "empty.docx");
        using (var document = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            document.Save();
        }

        var result = await _service.ImportAsync(filePath);

        Assert.NotNull(result);
        Assert.Equal(0, result.TotalParagraphs);
        Assert.Empty(result.Segments);
        Assert.Null(result.Error);
        Assert.Equal("empty.docx", result.FileName);
    }

    [Fact]
    public async Task ImportAsync_SingleParagraph_ReturnsOneSegment()
    {
        var filePath = CreateDocxWithParagraphs(_testDir, "single.docx",
            new[] { "Hello world" });

        var result = await _service.ImportAsync(filePath);

        Assert.NotNull(result);
        Assert.Equal(1, result.TotalParagraphs);
        Assert.Single(result.Segments);
        Assert.Equal("Hello world", result.Segments[0].Text);
        Assert.Equal("Normal", result.Segments[0].Style);
        Assert.False(result.Segments[0].IsEmpty);
        Assert.Equal(0, result.Segments[0].Index);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ImportAsync_MultipleParagraphs_CorrectCountAndOrdering()
    {
        var paragraphs = new[]
        {
            "First paragraph.",
            "Second paragraph.",
            "Third paragraph.",
            "Fourth paragraph.",
            "Fifth paragraph."
        };

        var filePath = CreateDocxWithParagraphs(_testDir, "multi.docx", paragraphs);

        var result = await _service.ImportAsync(filePath);

        Assert.NotNull(result);
        Assert.Equal(5, result.TotalParagraphs);
        Assert.Equal(5, result.Segments.Count);

        for (var i = 0; i < paragraphs.Length; i++)
        {
            Assert.Equal(paragraphs[i], result.Segments[i].Text);
            Assert.Equal(i, result.Segments[i].Index);
            Assert.False(result.Segments[i].IsEmpty);
        }

        // Verify line numbering is sequential
        Assert.Equal(1, result.Segments[0].StartLineNumber);
        Assert.Equal(5, result.Segments[4].EndLineNumber);
    }

    [Fact]
    public async Task ImportAsync_ArabicText_PreservedCorrectly()
    {
        var arabicParagraphs = new[]
        {
            "مرحبا بالعالم",
            "هذا نص عربي للتوضيح",
            "اختبار الترجمة من الإنجليزية إلى العربية"
        };

        var filePath = CreateDocxWithParagraphs(_testDir, "arabic.docx", arabicParagraphs);

        var result = await _service.ImportAsync(filePath);

        Assert.NotNull(result);
        Assert.Equal(3, result.TotalParagraphs);
        Assert.Equal("مرحبا بالعالم", result.Segments[0].Text);
        Assert.Equal("هذا نص عربي للتوضيح", result.Segments[1].Text);
        Assert.Equal("اختبار الترجمة من الإنجليزية إلى العربية", result.Segments[2].Text);
    }

    [Fact]
    public async Task ImportAsync_StyledParagraphs_DetectsStyles()
    {
        var filePath = CreateDocxWithStyles(_testDir, "styled.docx");

        var result = await _service.ImportAsync(filePath);

        Assert.NotNull(result);
        Assert.Equal(5, result.TotalParagraphs);

        // Normal paragraph
        Assert.Equal("Normal", result.Segments[0].Style);
        Assert.Equal("This is a normal paragraph.", result.Segments[0].Text);

        // Heading1
        Assert.Equal("Heading1", result.Segments[1].Style);
        Assert.Equal("Chapter One", result.Segments[1].Text);

        // Another normal paragraph
        Assert.Equal("Normal", result.Segments[2].Style);
        Assert.Equal("Body text under the heading.", result.Segments[2].Text);

        // Empty paragraph
        Assert.Equal("Normal", result.Segments[3].Style);
        Assert.True(result.Segments[3].IsEmpty);

        // Heading2
        Assert.Equal("Heading2", result.Segments[4].Style);
        Assert.Equal("Section 1.1", result.Segments[4].Text);
    }

    [Fact]
    public async Task ImportAsync_MixedArabicAndEnglish_PreservesBoth()
    {
        var mixedParagraphs = new[]
        {
            "The Great Pyramid of Giza — أهرام الجيزة العظيمة",
            "Translation is an art form.",
            "الترجمة فن من الفنون الجميلة"
        };

        var filePath = CreateDocxWithParagraphs(_testDir, "mixed.docx", mixedParagraphs);

        var result = await _service.ImportAsync(filePath);

        Assert.NotNull(result);
        Assert.Equal(3, result.TotalParagraphs);
        Assert.Equal("The Great Pyramid of Giza — أهرام الجيزة العظيمة", result.Segments[0].Text);
        Assert.Equal("Translation is an art form.", result.Segments[1].Text);
        Assert.Equal("الترجمة فن من الفنون الجميلة", result.Segments[2].Text);
    }

    [Fact]
    public async Task ImportAsync_NonExistentFile_ReturnsError()
    {
        var filePath = Path.Combine(_testDir, "nonexistent.docx");

        var result = await _service.ImportAsync(filePath);

        Assert.NotNull(result);
        Assert.NotNull(result.Error);
        Assert.Contains("not found", result.Error);
        Assert.Equal(0, result.TotalParagraphs);
        Assert.Empty(result.Segments);
    }

    [Fact]
    public async Task ImportAsync_UnsupportedFormat_ReturnsError()
    {
        // Create a non-.docx file
        var filePath = Path.Combine(_testDir, "test.txt");
        await File.WriteAllTextAsync(filePath, "Not a docx file");

        var result = await _service.ImportAsync(filePath);

        Assert.NotNull(result);
        Assert.NotNull(result.Error);
        Assert.Contains("Unsupported", result.Error);
    }

    [Fact]
    public async Task ImportAsync_Cancellation_ThrowsOperationCanceledException()
    {
        var filePath = CreateDocxWithParagraphs(_testDir, "cancel.docx",
            new[] { "Test paragraph" });

        var cts = new CancellationTokenSource();

        // Cancel immediately
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.ImportAsync(filePath, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task ImportAsync_ProgressCallback_ReceivesUpdates()
    {
        var progressReports = new List<(double Progress, string Text)>();
        var progress = new Progress<(double Progress, string Text)>(p =>
        {
            progressReports.Add(p);
        });

        var filePath = CreateDocxWithParagraphs(_testDir, "progress.docx",
            Enumerable.Range(0, 60).Select(i => $"Paragraph {i + 1}").ToArray());

        await _service.ImportAsync(filePath, progress);

        // Should have received at least one progress report
        Assert.NotEmpty(progressReports);

        // Last progress should be near 100%
        var lastProgress = progressReports[^1];
        Assert.True(lastProgress.Progress >= 95.0,
            $"Expected last progress >= 95% but got {lastProgress.Progress}%");
    }

    [Fact]
    public async Task ImportAsync_ResultFilePathAndFileName_Correct()
    {
        var filePath = CreateDocxWithParagraphs(_testDir, "myfile.docx",
            new[] { "Test content" });

        var result = await _service.ImportAsync(filePath);

        Assert.Equal(filePath, result.FilePath);
        Assert.Equal("myfile.docx", result.FileName);
    }
}
