using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RDAT.Copilot.Core.Models;
using RDAT.Copilot.Core.Services;

namespace RDAT.Copilot.Infrastructure.Linting;

/// <summary>
/// A high-performance string matching engine utilizing a compiled Regex 
/// mimicking Aho-Corasick behavior for rapid multi-keyword detection.
/// </summary>
public sealed class AmtaLinterService : IAmtaLinterService
{
    private readonly object _syncRoot = new();
    private Dictionary<string, string> _glossaryMap = new(StringComparer.OrdinalIgnoreCase);
    private Regex? _searchRegex;
    private GlossaryInfo _loadedInfo = new("None", 0, DateTime.MinValue);

    public async ValueTask LoadGlossaryAsync(string glossaryPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(glossaryPath))
            return;

        using var stream = File.OpenRead(glossaryPath);
        var entries = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, cancellationToken: cancellationToken);
        
        if (entries == null) return;

        lock (_syncRoot)
        {
            _glossaryMap = new Dictionary<string, string>(entries, StringComparer.OrdinalIgnoreCase);
            
            // Build a compiled Regex for Aho-Corasick like O(N) multi-pattern matching
            // Escapes terms to prevent regex injection
            var pattern = string.Join("|", _glossaryMap.Keys.Select(k => $@"\b{Regex.Escape(k)}\b"));
            _searchRegex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

            _loadedInfo = new GlossaryInfo(Path.GetFileName(glossaryPath), _glossaryMap.Count, DateTime.UtcNow);
        }
    }

    public LintResult Lint(string translationText, string languagePair)
    {
        var regex = _searchRegex;
        if (string.IsNullOrWhiteSpace(translationText) || regex == null)
            return LintResult.Pass;

        var violations = new List<GlossaryViolation>();
        var matches = regex.Matches(translationText);

        foreach (Match match in matches)
        {
            if (_glossaryMap.TryGetValue(match.Value, out var requiredTerm))
            {
                // A violation occurs if the text uses a forbidden term but we have a preferred one
                // Wait, the logic request: "If user's glossary says Bank -> مصرف and AI suggests بنك, linter must detect."
                // Since this requires synonyms mapped to forbidden terms, our JSON structure maps Forbidden -> Required.
                // i.e., "بنك" -> "مصرف".
                violations.Add(new GlossaryViolation(match.Value, requiredTerm, match.Index, match.Length, ViolationSeverity.Error));
            }
        }

        if (violations.Count == 0)
            return LintResult.Pass;

        return new LintResult(false, violations);
    }

    public string? TryAutoCorrect(string translationText, string languagePair, IReadOnlyList<GlossaryViolation> violations)
    {
        if (string.IsNullOrWhiteSpace(translationText) || violations == null || violations.Count == 0)
            return null;

        // Auto-correct by replacing forbidden terms with required terms from end to start to prevent offset shifts
        var correctedText = translationText;
        foreach (var v in violations.OrderByDescending(v => v.StartIndex))
        {
            correctedText = correctedText.Remove(v.StartIndex, v.Length).Insert(v.StartIndex, v.RequiredTerm);
        }

        return correctedText;
    }

    public GlossaryInfo GetLoadedGlossaryInfo()
    {
        lock (_syncRoot)
        {
            return _loadedInfo;
        }
    }
}
