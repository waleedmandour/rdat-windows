using RDAT.Copilot.Core.Models;
using Xunit;

namespace RDAT.Copilot.Core.Tests;

public class TranslationMemoryTests
{
    [Fact]
    public void TmEntry_CreatesWithDefaults()
    {
        var entry = new TmEntry(
            Id: "test-001",
            SourceText: "Hello world",
            TargetText: "مرحبا بالعالم"
        );

        Assert.Equal("test-001", entry.Id);
        Assert.Equal("Hello world", entry.SourceText);
        Assert.Equal("مرحبا بالعالم", entry.TargetText);
        Assert.Equal("en", entry.SourceLanguage);
        Assert.Equal("ar", entry.TargetLanguage);
        Assert.Null(entry.Domain);
        Assert.Equal(1.0, entry.QualityScore);
        Assert.Null(entry.CreatedAt);
    }

    [Fact]
    public void TmEntry_CreatesWithCustomLanguagePair()
    {
        var entry = new TmEntry(
            Id: "test-002",
            SourceText: "مرحبا",
            TargetText: "Hello",
            SourceLanguage: "ar",
            TargetLanguage: "en",
            Domain: "General",
            QualityScore: 0.85,
            CreatedAt: DateTime.UtcNow
        );

        Assert.Equal("ar", entry.SourceLanguage);
        Assert.Equal("en", entry.TargetLanguage);
        Assert.Equal("General", entry.Domain);
        Assert.Equal(0.85, entry.QualityScore);
        Assert.NotNull(entry.CreatedAt);
    }

    [Fact]
    public void TmSearchResult_HoldsEntryAndScore()
    {
        var entry = new TmEntry("id-1", "source", "target");
        var result = new TmSearchResult(entry, Score: 0.92, SearchMs: 12.5);

        Assert.Equal(0.92, result.Score);
        Assert.Equal(12.5, result.SearchMs);
        Assert.Same(entry, result.Entry);
    }

    [Fact]
    public void TmImportResult_ReportsCorrectCounts()
    {
        var result = new TmImportResult(
            TotalRows: 100,
            ImportedCount: 95,
            SkippedCount: 3,
            ErrorCount: 2,
            Errors: new List<string> { "Row 50: invalid format", "Row 75: missing target" },
            ElapsedMs: 5000.0
        );

        Assert.Equal(100, result.TotalRows);
        Assert.Equal(95, result.ImportedCount);
        Assert.Equal(3, result.SkippedCount);
        Assert.Equal(2, result.ErrorCount);
        Assert.Equal(2, result.Errors.Count);
        Assert.Equal(5000.0, result.ElapsedMs);
    }

    [Fact]
    public void LanceTmRow_FromTmEntry_PreservesFields()
    {
        var entry = new TmEntry(
            Id: "conv-001",
            SourceText: "The quick brown fox",
            TargetText: "الثعلب البني السريع",
            SourceLanguage: "en",
            TargetLanguage: "ar",
            Domain: "General",
            QualityScore: 0.95,
            CreatedAt: new DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc)
        );

        var row = LanceTmRow.FromTmEntry(entry);

        Assert.Equal("conv-001", row.Id);
        Assert.Equal("The quick brown fox", row.SourceText);
        Assert.Equal("الثعلب البني السريع", row.TargetText);
        Assert.Equal("en", row.SourceLanguage);
        Assert.Equal("ar", row.TargetLanguage);
        Assert.Equal("General", row.Domain);
        Assert.Equal(0.95, row.QualityScore);
        Assert.Equal(new DateTimeOffset(entry.CreatedAt.Value).ToUnixTimeSeconds(), row.CreatedAtUnix);
    }

    [Fact]
    public void LanceTmRoundTrip_PreservesData()
    {
        var original = new TmEntry(
            Id: "round-trip-001",
            SourceText: "Translation memory technology",
            TargetText: "تقنية ذاكرة الترجمة",
            SourceLanguage: "en",
            TargetLanguage: "ar"
        );

        var row = LanceTmRow.FromTmEntry(original);
        var restored = row.ToTmEntry();

        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.SourceText, restored.SourceText);
        Assert.Equal(original.TargetText, restored.TargetText);
        Assert.Equal(original.SourceLanguage, restored.SourceLanguage);
        Assert.Equal(original.TargetLanguage, restored.TargetLanguage);
        Assert.Equal(original.Domain, restored.Domain);
        Assert.Equal(original.QualityScore, restored.QualityScore);
    }

    [Fact]
    public void TmStats_ReportsCorrectBreakdown()
    {
        var stats = new TmStats(
            TotalEntries: 50000,
            DomainBreakdown: new Dictionary<string, int>
            {
                { "Legal", 20000 },
                { "Medical", 15000 },
                { "General", 15000 }
            },
            LanguagePairs: new Dictionary<string, int>
            {
                { "en→ar", 35000 },
                { "ar→en", 15000 }
            },
            DbSizeMb: 125.5
        );

        Assert.Equal(50000, stats.TotalEntries);
        Assert.Equal(3, stats.DomainBreakdown.Count);
        Assert.Equal(20000, stats.DomainBreakdown["Legal"]);
        Assert.Equal(2, stats.LanguagePairs.Count);
        Assert.Equal(125.5, stats.DbSizeMb);
    }

    [Theory]
    [InlineData("test.csv", TmImportFormat.Csv)]
    [InlineData("test.TMX", TmImportFormat.Tmx)]
    [InlineData("data.tsv", TmImportFormat.Tsv)]
    [InlineData("notes.txt", TmImportFormat.Tsv)]
    public void TmImportFormat_MapsExtensions(string fileName, TmImportFormat expected)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var format = ext switch
        {
            ".csv" => TmImportFormat.Csv,
            ".tmx" => TmImportFormat.Tmx,
            ".tsv" => TmImportFormat.Tsv,
            ".txt" => TmImportFormat.Tsv,
            _ => throw new NotSupportedException()
        };

        Assert.Equal(expected, format);
    }
}
