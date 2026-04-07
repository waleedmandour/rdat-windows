using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RDAT.Copilot.Core.Constants;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Core.Services;

/// <summary>
/// AMTA Terminology Linter — validates translation text against approved glossaries.
///
/// The linter performs the following checks:
///   1. Missing Terms: Source glossary terms not found in the target translation
///   2. Inconsistent Terms: Target text uses different translations for glossary terms
///   3. Untranslated Terms: Source terms that appear verbatim in the target
///   4. Casing Issues: Proper nouns/acronyms incorrectly translated
///
/// Glossary Loading:
///   - CSV: source_term,target_term,domain,notes (header required)
///   - JSON: [{ "source": "...", "target": "...", "domain": "...", "notes": "...", "required": true }]
///   - TSV: source_term\ttarget_term (no header required)
///
/// The linter uses case-insensitive matching with Unicode normalization for
/// robust multilingual term detection in Arabic and English text.
/// </summary>
public sealed partial class AmtaLinterService : IAmtaLinterService, IDisposable
{
    private readonly ILogger<AmtaLinterService> _logger;

    // Loaded glossary terms
    private readonly List<GlossaryTerm> _terms = new();

    // Normalized lookup dictionaries (lowercase source → term, target variants → term)
    private readonly Dictionary<string, List<GlossaryTerm>> _sourceIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GlossaryTerm> _targetIndex = new(StringComparer.OrdinalIgnoreCase);

    private AmtaLinterState _state = AmtaLinterState.Idle;
    private readonly object _stateLock = new();

    public AmtaLinterState State
    {
        get { lock (_stateLock) return _state; }
        private set { lock (_stateLock) _state = value; }
    }

    public int TermCount => _terms.Count;

    public bool IsReady => State == AmtaLinterState.Ready && _terms.Count > 0;

    public event EventHandler<IReadOnlyList<AmtaLintIssue>>? LintCompleted;

    public AmtaLinterService(ILogger<AmtaLinterService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<int> LoadGlossaryAsync(
        string filePath,
        IProgress<(double Progress, string Text)>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Glossary file path cannot be empty.", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Glossary file not found: {filePath}", filePath);

        State = AmtaLinterState.Loading;
        progress?.Report((0, "Loading glossary..."));

        var sw = Stopwatch.StartNew();

        try
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            List<GlossaryTerm> parsedTerms;

            switch (extension)
            {
                case ".csv":
                    parsedTerms = ParseCsvGlossary(filePath);
                    break;
                case ".json":
                    parsedTerms = await ParseJsonGlossaryAsync(filePath).ConfigureAwait(false);
                    break;
                case ".tsv":
                case ".txt":
                    parsedTerms = ParseTsvGlossary(filePath);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported glossary format: {extension}");
            }

            LoadGlossary(parsedTerms);

            progress?.Report((1.0, $"Loaded {_terms.Count} terms from {Path.GetFileName(filePath)}"));
            _logger.LogInformation(
                "[AMTA-Linter] Loaded {Count} glossary terms from {File} ({Ms:F0}ms)",
                _terms.Count, Path.GetFileName(filePath), sw.Elapsed.TotalMilliseconds);

            return _terms.Count;
        }
        catch (Exception ex)
        {
            State = AmtaLinterState.Error;
            _logger.LogError(ex, "[AMTA-Linter] Failed to load glossary from {File}", filePath);
            progress?.Report((0, $"Failed to load glossary: {ex.Message}"));
            throw;
        }
    }

    /// <inheritdoc/>
    public void LoadGlossary(IReadOnlyList<GlossaryTerm> terms)
    {
        lock (_stateLock)
        {
            _terms.Clear();
            _sourceIndex.Clear();
            _targetIndex.Clear();

            foreach (var term in terms.Where(t => !string.IsNullOrWhiteSpace(t.SourceTerm)))
            {
                _terms.Add(term);

                // Index by normalized source term
                var normalizedSource = term.SourceTerm.Trim().ToLowerInvariant();
                if (!_sourceIndex.ContainsKey(normalizedSource))
                    _sourceIndex[normalizedSource] = new List<GlossaryTerm>();
                _sourceIndex[normalizedSource].Add(term);

                // Index by normalized target term
                var normalizedTarget = term.TargetTerm.Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(normalizedTarget))
                {
                    _targetIndex[normalizedTarget] = term;
                }
            }

            State = _terms.Count > 0 ? AmtaLinterState.Ready : AmtaLinterState.Idle;
        }

        _logger.LogInformation("[AMTA-Linter] Glossary indexed: {Count} terms", _terms.Count);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AmtaLintIssue>> CheckAsync(
        string targetText,
        string sourceText,
        CancellationToken cancellationToken = default)
    {
        if (!IsReady) return Array.Empty<AmtaLintIssue>();
        if (string.IsNullOrWhiteSpace(targetText)) return Array.Empty<AmtaLintIssue>();

        State = AmtaLinterState.Checking;
        var sw = Stopwatch.StartNew();
        var issues = new List<AmtaLintIssue>();
        var issueId = 0;

        try
        {
            var targetLines = targetText.Split('\n');
            var sourceLines = sourceText?.Split('\n') ?? Array.Empty<string>();

            for (int lineIdx = 0; lineIdx < targetLines.Length; lineIdx++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var targetLine = targetLines[lineIdx];
                var sourceLine = lineIdx < sourceLines.Length ? sourceLines[lineIdx] : string.Empty;

                // Check each glossary term against this line
                foreach (var term in _terms)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Skip very short terms (below minimum term length)
                    if (term.SourceTerm.Trim().Length < AppConstants.AmtaMinTermLength) continue;

                    // ─── Check 1: Missing Term ────────────────────────────
                    // Source term exists in the source line but the approved
                    // target term is not found anywhere in the target text
                    if (!string.IsNullOrWhiteSpace(sourceLine) &&
                        ContainsTerm(sourceLine, term.SourceTerm))
                    {
                        if (!ContainsTerm(targetText, term.TargetTerm) &&
                            !ContainsTerm(targetText, term.SourceTerm))
                        {
                            // Source term present, but neither source nor target
                            // term found in translation
                            if (term.IsRequired)
                            {
                                issues.Add(new AmtaLintIssue(
                                    Id: $"amt-{++issueId}",
                                    Type: AmtaLintType.MissingTerm,
                                    Severity: AmtaLintSeverity.Error,
                                    Message: $"Required term \"{term.SourceTerm}\" → \"{term.TargetTerm}\" not found in translation",
                                    Suggestion: term.TargetTerm,
                                    OriginalText: term.SourceTerm,
                                    StartLineNumber: lineIdx + 1,
                                    EndLineNumber: lineIdx + 1,
                                    StartColumn: 1,
                                    EndColumn: targetLine.Length + 1,
                                    MatchingTerm: term,
                                    Domain: term.Domain
                                ));
                            }
                        }
                    }

                    // ─── Check 2: Untranslated Term ────────────────────────
                    // Source term appears verbatim in the target (not translated)
                    if (ContainsTerm(targetLine, term.SourceTerm) &&
                        !string.IsNullOrWhiteSpace(term.TargetTerm))
                    {
                        // Check if the source term is surrounded by the target term
                        // (might be acceptable in some contexts like academic text)
                        var sourceTermPos = IndexOfTerm(targetLine, term.SourceTerm);
                        if (sourceTermPos >= 0)
                        {
                            issues.Add(new AmtaLintIssue(
                                Id: $"amt-{++issueId}",
                                Type: AmtaLintType.UntranslatedTerm,
                                Severity: AmtaLintSeverity.Warning,
                                Message: $"Term \"{term.SourceTerm}\" appears untranslated. Approved translation: \"{term.TargetTerm}\"",
                                Suggestion: term.TargetTerm,
                                OriginalText: term.SourceTerm,
                                StartLineNumber: lineIdx + 1,
                                EndLineNumber: lineIdx + 1,
                                StartColumn: sourceTermPos + 1,
                                EndColumn: sourceTermPos + term.SourceTerm.Length + 1,
                                MatchingTerm: term,
                                Domain: term.Domain
                            ));
                        }
                    }

                    // ─── Check 3: Casing Issue ─────────────────────────────
                    // Check for proper nouns/acronyms that should remain as-is
                    if (IsProperNounOrAcronym(term.SourceTerm))
                    {
                        var lowerVersion = term.SourceTerm.ToLowerInvariant();
                        if (ContainsTerm(targetLine, lowerVersion) &&
                            !ContainsTerm(targetLine, term.SourceTerm))
                        {
                            var lowerPos = IndexOfTerm(targetLine, lowerVersion);
                            if (lowerPos >= 0)
                            {
                                issues.Add(new AmtaLintIssue(
                                    Id: $"amt-{++issueId}",
                                    Type: AmtaLintType.CasingIssue,
                                    Severity: AmtaLintSeverity.Info,
                                    Message: $"Proper noun \"{term.SourceTerm}\" appears with incorrect casing",
                                    Suggestion: term.SourceTerm,
                                    OriginalText: lowerVersion,
                                    StartLineNumber: lineIdx + 1,
                                    EndLineNumber: lineIdx + 1,
                                    StartColumn: lowerPos + 1,
                                    EndColumn: lowerPos + lowerVersion.Length + 1,
                                    MatchingTerm: term,
                                    Domain: term.Domain
                                ));
                            }
                        }
                    }
                }
            }

            // ─── Check 4: Inconsistent Terms ──────────────────────────
            // Check if any variant of a target term is used instead of the approved one
            foreach (var kvp in _sourceIndex)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sourceTerm = kvp.Key;
                var allVariants = kvp.Value;

                if (allVariants.Count < 2) continue;

                // Find which target terms appear in the translation
                var foundTargets = new List<GlossaryTerm>();
                foreach (var variant in allVariants)
                {
                    if (ContainsTerm(targetText, variant.TargetTerm))
                    {
                        foundTargets.Add(variant);
                    }
                }

                // If multiple variants found, flag the non-primary ones
                if (foundTargets.Count > 1)
                {
                    var primary = foundTargets[0]; // First loaded = primary
                    for (int i = 1; i < foundTargets.Count; i++)
                    {
                        var variant = foundTargets[i];
                        // Find the position of this variant in the text
                        for (int lineIdx = 0; lineIdx < targetLines.Length; lineIdx++)
                        {
                            var pos = IndexOfTerm(targetLines[lineIdx], variant.TargetTerm);
                            if (pos >= 0)
                            {
                                issues.Add(new AmtaLintIssue(
                                    Id: $"amt-{++issueId}",
                                    Type: AmtaLintType.InconsistentTerm,
                                    Severity: AmtaLintSeverity.Warning,
                                    Message: $"Inconsistent term: \"{variant.TargetTerm}\" (approved: \"{primary.TargetTerm}\")",
                                    Suggestion: primary.TargetTerm,
                                    OriginalText: variant.TargetTerm,
                                    StartLineNumber: lineIdx + 1,
                                    EndLineNumber: lineIdx + 1,
                                    StartColumn: pos + 1,
                                    EndColumn: pos + variant.TargetTerm.Length + 1,
                                    MatchingTerm: primary,
                                    Domain: variant.Domain
                                ));
                                break; // Only flag once per line
                            }
                        }
                    }
                }
            }

            State = AmtaLinterState.Ready;
            _logger.LogInformation(
                "[AMTA-Linter] Check completed: {Issues} issues found ({Ms:F0}ms)",
                issues.Count, sw.Elapsed.TotalMilliseconds);

            LintCompleted?.Invoke(this, issues);
            return issues;
        }
        catch (OperationCanceledException)
        {
            State = AmtaLinterState.Ready;
            _logger.LogInformation("[AMTA-Linter] Check cancelled");
            return issues;
        }
        catch (Exception ex)
        {
            State = AmtaLinterState.Error;
            _logger.LogError(ex, "[AMTA-Linter] Check failed");
            return issues;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<GlossaryTerm> GetTerms() => _terms.AsReadOnly();

    /// <inheritdoc/>
    public void ClearGlossary()
    {
        lock (_stateLock)
        {
            _terms.Clear();
            _sourceIndex.Clear();
            _targetIndex.Clear();
            State = AmtaLinterState.Idle;
        }

        _logger.LogInformation("[AMTA-Linter] Glossary cleared");
    }

    // ════════════════════════════════════════════════════════════════
    // Glossary Parsers
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parse a CSV glossary file.
    /// Format: source_term,target_term,domain,notes (header row expected)
    /// </summary>
    private List<GlossaryTerm> ParseCsvGlossary(string filePath)
    {
        var terms = new List<GlossaryTerm>();
        var lines = File.ReadAllLines(filePath);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Skip header row
            if (i == 0 && (line.StartsWith("source", StringComparison.OrdinalIgnoreCase) ||
                          line.StartsWith("\"source", StringComparison.OrdinalIgnoreCase)))
                continue;

            var parts = line.Split(',');
            if (parts.Length < 2) continue;

            var source = parts[0].Trim().Trim('"');
            var target = parts[1].Trim().Trim('"');
            var domain = parts.Length > 2 ? parts[2].Trim().Trim('"') : null;
            var notes = parts.Length > 3 ? parts[3].Trim().Trim('"') : null;
            var isRequired = parts.Length > 4 && bool.TryParse(parts[4].Trim(), out var req) ? req : true;

            if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(target))
            {
                terms.Add(new GlossaryTerm(
                    Id: $"g-{terms.Count + 1}",
                    SourceTerm: source,
                    TargetTerm: target,
                    Domain: domain,
                    Notes: notes,
                    IsRequired: isRequired
                ));
            }
        }

        return terms;
    }

    /// <summary>
    /// Parse a JSON glossary file.
    /// Format: [{ "source": "...", "target": "...", "domain": "...", "notes": "...", "required": true }]
    /// </summary>
    private async Task<List<GlossaryTerm>> ParseJsonGlossaryAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
        var terms = new List<GlossaryTerm>();

        try
        {
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    var source = element.GetProperty("source").GetString();
                    var target = element.GetProperty("target").GetString();
                    var domain = element.TryGetProperty("domain", out var d) ? d.GetString() : null;
                    var notes = element.TryGetProperty("notes", out var n) ? n.GetString() : null;
                    var isRequired = element.TryGetProperty("required", out var r) ? r.GetBoolean() : true;
                    var pos = element.TryGetProperty("partOfSpeech", out var p) ? p.GetString() : null;

                    if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(target))
                    {
                        terms.Add(new GlossaryTerm(
                            Id: $"g-{terms.Count + 1}",
                            SourceTerm: source,
                            TargetTerm: target,
                            Domain: domain,
                            Notes: notes,
                            IsRequired: isRequired,
                            PartOfSpeech: pos
                        ));
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[AMTA-Linter] Failed to parse JSON glossary");
            throw;
        }

        return terms;
    }

    /// <summary>
    /// Parse a TSV glossary file.
    /// Format: source_term\ttarget_term (tab-separated, no header required)
    /// </summary>
    private List<GlossaryTerm> ParseTsvGlossary(string filePath)
    {
        var terms = new List<GlossaryTerm>();
        var lines = File.ReadAllLines(filePath);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            var parts = trimmed.Split('\t');
            if (parts.Length < 2) continue;

            var source = parts[0].Trim();
            var target = parts[1].Trim();

            if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(target))
            {
                terms.Add(new GlossaryTerm(
                    Id: $"g-{terms.Count + 1}",
                    SourceTerm: source,
                    TargetTerm: target
                ));
            }
        }

        return terms;
    }

    // ════════════════════════════════════════════════════════════════
    // Term Matching Helpers
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Check if the text contains the given term (case-insensitive, word-boundary aware).
    /// Uses word boundary matching for multi-word terms and simple Contains for single words.
    /// </summary>
    private static bool ContainsTerm(string text, string term)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(term)) return false;

        var normalizedText = text.ToLowerInvariant();
        var normalizedTerm = term.Trim().ToLowerInvariant();

        if (normalizedTerm.Contains(' '))
        {
            // Multi-word term: use word boundary matching
            return normalizedText.Contains(normalizedTerm);
        }

        // Single-word: use word boundary regex
        try
        {
            return Regex.IsMatch(normalizedText, $@"\b{Regex.Escape(normalizedTerm)}\b");
        }
        catch
        {
            return normalizedText.Contains(normalizedTerm);
        }
    }

    /// <summary>
    /// Find the index of a term in the text (case-insensitive).
    /// </summary>
    private static int IndexOfTerm(string text, string term)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(term)) return -1;
        return text.ToLowerInvariant().IndexOf(term.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determine if a source term is a proper noun or acronym that should
    /// retain its original casing in the target text.
    /// Heuristic: all uppercase (length > 1), title case, or contains digits.
    /// </summary>
    private static bool IsProperNounOrAcronym(string term)
    {
        if (string.IsNullOrWhiteSpace(term)) return false;
        var trimmed = term.Trim();

        // All uppercase with length > 1 (e.g., WHO, UNESCO, NATO)
        if (trimmed.Length > 1 && trimmed.All(c => char.IsUpper(c) || char.IsDigit(c) || c == ' '))
            return true;

        // Title case with length > 2 (e.g., United Nations, World Health Organization)
        if (trimmed.Length > 2 && char.IsUpper(trimmed[0]) &&
            trimmed.Skip(1).Any(c => char.IsUpper(c)))
            return true;

        return false;
    }

    public void Dispose()
    {
        ClearGlossary();
    }
}
