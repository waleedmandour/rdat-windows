// ========================================================================
// RDAT Copilot - AMTA Glossary Linter (Aho-Corasick)
// Location: src/RDAT.Copilot.Infrastructure/Linting/AmtaLinterService.cs
// ========================================================================
// High-performance terminology enforcement using the Aho-Corasick algorithm.
// Builds a finite state machine from forbidden terms + known mistranslations,
// enabling O(n) multi-pattern matching of every AI prediction.
//
// Privacy: 100% offline - no network calls.
// ========================================================================

using System.Collections.Concurrent;
using System.Text.Json;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Infrastructure.Linting;

/// <summary>
/// Aho-Corasick automaton node for multi-pattern string matching.
/// </summary>
internal sealed class AhoNode
{
    public Dictionary<char, AhoNode> Children { get; } = new();
    public AhoNode? Failure { get; set; }
    public AhoNode? DictSuffix { get; set; }
    public List<GlossaryEntry>? Output { get; set; }
    public int Depth { get; set; }
}

/// <summary>
/// High-performance AMTA glossary linter using Aho-Corasick for O(n+m)
/// multi-pattern matching. Thread-safe via ReaderWriterLockSlim.
/// </summary>
public sealed class AmtaLinterService : IAmtaLinterService
{
    private AhoNode _root = new();
    private bool _isBuilt;
    private readonly ReaderWriterLockSlim _buildLock = new();
    private List<GlossaryEntry> _glossary = new();
    private readonly string? _glossaryPath;

    // Configuration
    public bool AutoCorrectEnabled { get; set; } = true;
    public bool SuppressOnViolation { get; set; } = true;
    public int MaxCorrectionsPerSuggestion { get; set; } = 10;

    public event Action<GlossaryViolation>? OnViolationDetected;
    public int GlossaryCount => _glossary.Count;

    public async Task LoadGlossaryAsync(string glossaryPath, CancellationToken ct = default)
    {
        _glossaryPath = glossaryPath;
        await Task.Run(() => InitializeGlossary(glossaryPath), ct);
    }

    public LintResult Lint(string suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion))
            return new LintResult { IsClean = true };

        EnsureBuilt();
        var violations = Scan(suggestion);
        var result = new LintResult { IsClean = violations.Count == 0, Violations = violations };

        foreach (var v in violations)
            OnViolationDetected?.Invoke(v);

        return result;
    }

    public bool TryAutoCorrect(ref string suggestion, out string corrected)
    {
        if (string.IsNullOrWhiteSpace(suggestion))
        {
            corrected = suggestion;
            return false;
        }

        EnsureBuilt();
        var violations = Scan(suggestion);
        if (violations.Count == 0)
        {
            corrected = suggestion;
            return false;
        }

        corrected = suggestion;
        var sorted = violations.OrderByDescending(v => v.Length).ThenBy(v => v.StartIndex).ToList();
        int offset = 0;
        int count = 0;

        foreach (var v in sorted)
        {
            if (count >= MaxCorrectionsPerSuggestion) break;
            string replacement = v.Entry.Direction == "en->ar"
                ? v.Entry.TargetTerm
                : v.Entry.SourceTerm;
            corrected = corrected.Remove(v.StartIndex + offset, v.Length)
                .Insert(v.StartIndex + offset, replacement);
            v.WasAutoCorrected = true;
            offset += replacement.Length - v.Length;
            count++;
        }

        return count > 0;
    }

    public LintResult LintAndCorrect(ref string suggestion)
    {
        var result = Lint(suggestion);
        if (result.IsClean)
        {
            result.CorrectedText = suggestion;
            return result;
        }

        if (AutoCorrectEnabled)
        {
            TryAutoCorrect(ref suggestion, out string corrected);
            result.CorrectedText = corrected;
            result.ShouldSuppress = false;
        }
        else if (SuppressOnViolation)
        {
            result.CorrectedText = null;
            result.ShouldSuppress = true;
        }
        else
        {
            result.CorrectedText = suggestion;
            result.ShouldSuppress = false;
        }

        return result;
    }

    public void ReloadGlossary()
    {
        if (_glossaryPath is null) return;
        _buildLock.EnterWriteLock();
        try
        {
            _isBuilt = false;
            _glossary.Clear();
            _root = new AhoNode();
            InitializeGlossary(_glossaryPath);
        }
        finally { _buildLock.ExitWriteLock(); }
    }

    // ====================================================================
    // Aho-Corasick Construction
    // ====================================================================

    private void InitializeGlossary(string path)
    {
        if (!File.Exists(path)) return;

        var json = File.ReadAllText(path);
        var entries = JsonSerializer.Deserialize<List<GlossaryEntry>>(json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder
                    .Create(System.Text.Unicode.UnicodeRanges.All)
            });

        if (entries is null) return;
        _glossary = entries;
    }

    private void EnsureBuilt()
    {
        if (_isBuilt) return;
        _buildLock.EnterUpgradeableReadLock();
        try { if (!_isBuilt) BuildAutomaton(); }
        finally { _buildLock.ExitUpgradeableReadLock(); }
    }

    private void BuildAutomaton()
    {
        _root = new AhoNode();
        foreach (var entry in _glossary)
        {
            AddPattern(entry, entry.ForbiddenTerm ?? entry.SourceTerm);
            if (entry.KnownMistranslations is not null)
                foreach (var mt in entry.KnownMistranslations)
                    AddPattern(entry, mt);
        }

        // BFS failure links
        var queue = new Queue<AhoNode>();
        foreach (var c in _root.Children.Values) { c.Failure = _root; queue.Enqueue(c); }

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var (ch, child) in cur.Children)
            {
                var fail = cur.Failure;
                while (fail is not null && !fail.Children.ContainsKey(ch))
                    fail = fail.Failure;
                child.Failure = fail?.Children.GetValueOrDefault(ch) ?? _root;
                if (child.Failure?.Output is { Count: > 0 })
                    child.DictSuffix = child.Failure;
                queue.Enqueue(child);
            }
        }
        _isBuilt = true;
    }

    private void AddPattern(GlossaryEntry entry, string term)
    {
        if (string.IsNullOrEmpty(term)) return;
        var node = _root;
        foreach (var ch in term)
        {
            if (!node.Children.TryGetValue(ch, out var child))
            {
                child = new AhoNode { Depth = node.Depth + 1 };
                node.Children[ch] = child;
            }
            node = child;
        }
        node.Output ??= new List<GlossaryEntry>();
        node.Output.Add(entry);
    }

    // ====================================================================
    // Scanning
    // ====================================================================

    private List<GlossaryViolation> Scan(string text)
    {
        var violations = new List<GlossaryViolation>();
        var cur = _root;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            while (cur != _root && !cur.Children.ContainsKey(ch))
                cur = cur.Failure!;
            cur = cur.Children.TryGetValue(ch, out var child) ? child : _root;

            for (var outNode = cur; outNode is not null; outNode = outNode.DictSuffix)
            {
                if (outNode.Output is null) continue;
                foreach (var entry in outNode.Output)
                {
                    int len = outNode.Depth;
                    int start = i - len + 1;
                    violations.Add(new GlossaryViolation
                    {
                        Entry = entry,
                        ForbiddenTerm = text.Substring(start, len),
                        StartIndex = start,
                        Length = len
                    });
                }
            }
        }
        return Deduplicate(violations);
    }

    private static List<GlossaryViolation> Deduplicate(List<GlossaryViolation> v)
    {
        if (v.Count <= 1) return v;
        v.Sort((a, b) => a.StartIndex != b.StartIndex
            ? a.StartIndex.CompareTo(b.StartIndex)
            : b.Length.CompareTo(a.Length));
        var result = new List<GlossaryViolation>();
        int lastEnd = -1;
        foreach (var vi in v)
        {
            if (vi.StartIndex >= lastEnd)
            { result.Add(vi); lastEnd = vi.StartIndex + vi.Length; }
        }
        return result;
    }

    public void Dispose() => _buildLock.Dispose();
}
