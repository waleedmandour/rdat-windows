using System.Xml.Linq;
using CsvHelper;
using System.Globalization;
using Microsoft.Extensions.Logging;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Core.Services;

/// <summary>
/// Translation Memory file import service.
/// Supports TMX 1.4, CSV, and TSV file formats.
/// Parses translation units into <see cref="TmEntry"/> records.
/// </summary>
public sealed class TmImportService : ITmImportService
{
    private readonly ILogger<TmImportService>? _logger;

    public TmImportService(ILogger<TmImportService>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public TmImportFormat DetectFormat(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".csv" => TmImportFormat.Csv,
            ".tmx" => TmImportFormat.Tmx,
            ".tsv" => TmImportFormat.Tsv,
            ".txt" => TmImportFormat.Tsv, // Assume TSV for .txt
            _ => throw new NotSupportedException(
                $"Unsupported TM file format: {ext}. Supported formats: .csv, .tmx, .tsv")
        };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TmEntry>> ParseAsync(
        string filePath,
        string sourceLanguage = "en",
        string targetLanguage = "ar",
        string? domain = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"TM file not found: {filePath}", filePath);

        var format = DetectFormat(filePath);

        return format switch
        {
            TmImportFormat.Csv => await ParseCsvAsync(filePath, sourceLanguage, targetLanguage, domain, cancellationToken),
            TmImportFormat.Tmx => await ParseTmxAsync(filePath, sourceLanguage, targetLanguage, domain, cancellationToken),
            TmImportFormat.Tsv => await ParseTsvAsync(filePath, sourceLanguage, targetLanguage, domain, cancellationToken),
            _ => throw new NotSupportedException($"Format not supported: {format}")
        };
    }

    /// <inheritdoc/>
    public async Task<int> EstimateCountAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (!File.Exists(filePath)) return 0;

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var format = DetectFormat(filePath);

            return format switch
            {
                TmImportFormat.Tsv => File.ReadAllLines(filePath).Count(line =>
                    !string.IsNullOrWhiteSpace(line)),

                TmImportFormat.Csv => File.ReadAllLines(filePath).Count(line =>
                    !string.IsNullOrWhiteSpace(line)) - 1, // Minus header

                TmImportFormat.Tmx => CountTmxUnits(filePath),

                _ => 0
            };
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Parse a CSV file with columns: source, target (optional: domain, quality).
    /// Uses CsvHelper for robust parsing with quote handling and BOM support.
    /// </summary>
    private async Task<IReadOnlyList<TmEntry>> ParseCsvAsync(
        string filePath,
        string sourceLanguage,
        string targetLanguage,
        string? domain,
        CancellationToken cancellationToken)
    {
        var entries = new List<TmEntry>();

        await Task.Run(() =>
        {
            using var reader = new StreamReader(filePath, System.Text.Encoding.UTF8);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

            // Read header
            csv.Read();
            csv.ReadHeader();

            var sourceCol = GetColumnName(csv.Context.HeaderRecord, "source", "source_text", "english", "en");
            var targetCol = GetColumnName(csv.Context.HeaderRecord, "target", "target_text", "arabic", "ar");
            var domainCol = GetColumnName(csv.Context.HeaderRecord, "domain", "category", "subject");
            var qualityCol = GetColumnName(csv.Context.HeaderRecord, "quality", "score", "confidence");

            while (csv.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sourceText = csv.GetField(sourceCol)?.Trim();
                var targetText = csv.GetField(targetCol)?.Trim();

                if (string.IsNullOrWhiteSpace(sourceText) || string.IsNullOrWhiteSpace(targetText))
                    continue;

                var entryDomain = !string.IsNullOrEmpty(domainCol) && csv.TryGetField(domainCol, out string? d) ? d : domain;
                var quality = !string.IsNullOrEmpty(qualityCol) && csv.TryGetField(qualityCol, out double q) ? q : 1.0;

                entries.Add(new TmEntry(
                    Id: Guid.NewGuid().ToString("N"),
                    SourceText: sourceText,
                    TargetText: targetText,
                    SourceLanguage: sourceLanguage,
                    TargetLanguage: targetLanguage,
                    Domain: entryDomain,
                    QualityScore: Math.Clamp(quality, 0.0, 1.0),
                    CreatedAt: DateTime.UtcNow
                ));
            }
        }).ConfigureAwait(false);

        return entries;
    }

    /// <summary>
    /// Parse a TMX 1.4 (Translation Memory eXchange) XML file.
    /// TMX is the industry standard for Translation Memory interchange.
    /// </summary>
    private async Task<IReadOnlyList<TmEntry>> ParseTmxAsync(
        string filePath,
        string sourceLanguage,
        string targetLanguage,
        string? domain,
        CancellationToken cancellationToken)
    {
        var entries = new List<TmEntry>();

        await Task.Run(() =>
        {
            var doc = XDocument.Load(filePath);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
            var tuElements = doc.Descendants(ns + "tu");

            foreach (var tu in tuElements)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var tuvs = tu.Elements(ns + "tuv").ToList();
                if (tuvs.Count < 2) continue;

                string? sourceText = null;
                string? targetText = null;

                foreach (var tuv in tuvs)
                {
                    var lang = (string?)tuv.Attribute("xml:lang")
                              ?? (string?)tuv.Attribute("lang")
                              ?? string.Empty;

                    var seg = tuv.Element(ns + "seg");
                    var text = seg?.Value.Trim();

                    if (string.IsNullOrEmpty(text)) continue;

                    // Match source/target by language code
                    if (lang.StartsWith(sourceLanguage, StringComparison.OrdinalIgnoreCase))
                    {
                        sourceText = text;
                    }
                    else if (lang.StartsWith(targetLanguage, StringComparison.OrdinalIgnoreCase))
                    {
                        targetText = text;
                    }
                }

                // If language matching failed, use positional order
                if (sourceText is null && tuvs.Count >= 2)
                {
                    sourceText = tuvs[0].Element(ns + "seg")?.Value.Trim();
                    targetText = tuvs[1].Element(ns + "seg")?.Value.Trim();
                }

                if (string.IsNullOrWhiteSpace(sourceText) || string.IsNullOrWhiteSpace(targetText))
                    continue;

                var tuId = (string?)tu.Attribute("tuid") ?? Guid.NewGuid().ToString("N");

                entries.Add(new TmEntry(
                    Id: tuId,
                    SourceText: sourceText,
                    TargetText: targetText,
                    SourceLanguage: sourceLanguage,
                    TargetLanguage: targetLanguage,
                    Domain: domain,
                    QualityScore: 1.0,
                    CreatedAt: DateTime.UtcNow
                ));
            }
        }).ConfigureAwait(false);

        return entries;
    }

    /// <summary>
    /// Parse a TSV (Tab-Separated Values) file with source\ttarget per line.
    /// Simple format commonly used for quick TM exchanges.
    /// </summary>
    private async Task<IReadOnlyList<TmEntry>> ParseTsvAsync(
        string filePath,
        string sourceLanguage,
        string targetLanguage,
        string? domain,
        CancellationToken cancellationToken)
    {
        var entries = new List<TmEntry>();

        await Task.Run(() =>
        {
            var lines = File.ReadAllLines(filePath, System.Text.Encoding.UTF8);

            foreach (var rawLine in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue; // Skip comments

                var parts = line.Split('\t');
                if (parts.Length < 2) continue;

                var sourceText = parts[0].Trim();
                var targetText = parts[1].Trim();

                if (string.IsNullOrEmpty(sourceText) || string.IsNullOrEmpty(targetText))
                    continue;

                entries.Add(new TmEntry(
                    Id: Guid.NewGuid().ToString("N"),
                    SourceText: sourceText,
                    TargetText: targetText,
                    SourceLanguage: sourceLanguage,
                    TargetLanguage: targetLanguage,
                    Domain: domain,
                    QualityScore: 1.0,
                    CreatedAt: DateTime.UtcNow
                ));
            }
        }).ConfigureAwait(false);

        return entries;
    }

    /// <summary>
    /// Count translation units in a TMX file without fully parsing.
    /// </summary>
    private static int CountTmxUnits(string filePath)
    {
        using var reader = new StreamReader(filePath, System.Text.Encoding.UTF8);
        var count = 0;
        var inTu = false;

        while (reader.ReadLine() is { } line)
        {
            if (line.Contains("<tu ", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("<tu>", StringComparison.OrdinalIgnoreCase))
            {
                inTu = true;
            }
            if (inTu && line.Contains("</tu>", StringComparison.OrdinalIgnoreCase))
            {
                count++;
                inTu = false;
            }
        }

        return count;
    }

    /// <summary>
    /// Finds the first matching column name from a list of candidates.
    /// </summary>
    private static string GetColumnName(string[]? headers, params string[] candidates)
    {
        if (headers is null) return candidates[0];

        foreach (var candidate in candidates)
        {
            var match = Array.Find(headers, h =>
                h.Equals(candidate, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        return candidates[0]; // Default to first candidate
    }
}
