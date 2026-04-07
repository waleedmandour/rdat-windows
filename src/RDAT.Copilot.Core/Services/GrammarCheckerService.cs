using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RDAT.Copilot.Core.Constants;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Core.Services;

/// <summary>
/// LLM-powered Grammar and Style Checker service (Phase 4).
///
/// Uses the existing LLM queue engine (via ILocalInferenceService) to perform
/// grammar, spelling, punctuation, and style checking on translation text.
///
/// How it works:
///   1. Receives a target text segment and source sentence for context
///   2. Builds a structured prompt requesting JSON-formatted grammar analysis
///   3. Sends the request through the LLM queue with Grammar priority (non-preemptible)
///   4. Parses the structured JSON response into typed GrammarIssue records
///   5. Maps issues to line/column positions in the original text
///
/// The checker handles both English and Arabic grammar, including:
///   - Arabic diacritics (tashkeel) and vowel marks
///   - Subject-verb agreement in Arabic
///   - Correct use of Arabic conjunctions and prepositions
///   - English spelling, grammar, and punctuation
///
/// Prompt Strategy:
///   The system prompt instructs the LLM to output a JSON array of issues
///   with exact position markers that can be mapped back to the text.
/// </summary>
public sealed class GrammarCheckerService : IGrammarCheckerService
{
    private readonly ILocalInferenceService _inferenceService;
    private readonly ILogger<GrammarCheckerService> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private GrammarCheckState _state = GrammarCheckState.Idle;

    public GrammarCheckState State
    {
        get => _state;
        private set => _state = value;
    }

    public GrammarCheckerService(
        ILocalInferenceService inferenceService,
        ILogger<GrammarCheckerService> logger)
    {
        _inferenceService = inferenceService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<GrammarIssue>> CheckAsync(
        string text,
        string sourceSentence,
        LanguageDirection languageDirection,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<GrammarIssue>();

        if (!_inferenceService.IsReady)
        {
            _logger.LogWarning("[Grammar] Check skipped — LLM not ready");
            return Array.Empty<GrammarIssue>();
        }

        State = GrammarCheckState.Checking;
        var sw = Stopwatch.StartNew();

        try
        {
            var systemPrompt = BuildSystemPrompt(languageDirection);
            var userMessage = BuildUserPrompt(text, sourceSentence, languageDirection);

            _logger.LogDebug(
                "[Grammar] Running check: {Lang}, {Chars} chars",
                languageDirection, text.Length);

            var rawResponse = await _inferenceService.GenerateAsync(
                systemPrompt,
                userMessage,
                AppConstants.GrammarCheckMaxTokens,
                0.15f, // Low temperature for deterministic grammar analysis
                cancellationToken).ConfigureAwait(true);

            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                State = GrammarCheckState.Ready;
                _logger.LogInformation("[Grammar] No issues found ({Ms:F0}ms)", sw.Elapsed.TotalMilliseconds);
                return Array.Empty<GrammarIssue>();
            }

            // Parse the structured JSON response
            var issues = ParseGrammarResponse(rawResponse, text, languageDirection);

            State = GrammarCheckState.Ready;
            _logger.LogInformation(
                "[Grammar] Check complete: {Count} issues found ({Ms:F0}ms)",
                issues.Count, sw.Elapsed.TotalMilliseconds);

            return issues;
        }
        catch (OperationCanceledException)
        {
            State = GrammarCheckState.Idle;
            _logger.LogInformation("[Grammar] Check cancelled");
            return Array.Empty<GrammarIssue>();
        }
        catch (Exception ex)
        {
            State = GrammarCheckState.Error;
            _logger.LogError(ex, "[Grammar] Check failed");
            return Array.Empty<GrammarIssue>();
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Prompt Engineering
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Build the system prompt for grammar checking.
    /// Instructs the LLM to output structured JSON for reliable parsing.
    /// </summary>
    private static string BuildSystemPrompt(LanguageDirection direction)
    {
        var targetLang = direction == LanguageDirection.EnToAr ? "Arabic" : "English";
        var sourceLang = direction == LanguageDirection.EnToAr ? "English" : "Arabic";

        return $"""
            You are a professional {targetLang} grammar and style checker for translation quality assurance.
            You review {targetLang} translations of {sourceLang} source text.

            Your task is to find grammar, spelling, punctuation, and style issues in the {targetLang} translation.
            Compare the translation against the source to ensure meaning is preserved.

            Output ONLY a JSON array of issues. Each issue must have:
            - "type": one of "spelling", "grammar", "punctuation", "style"
            - "message": brief description in English
            - "suggestion": the corrected {targetLang} text (or "")
            - "original": the problematic text fragment
            - "line": approximate line number (1-based)

            {"For Arabic: Check diacritics (tashkeel), subject-verb agreement, correct preposition usage, dual/plural forms, and idafa constructions."}

            If no issues are found, output: []

            Output ONLY the JSON array. No explanations, no markdown.
            """;
    }

    /// <summary>
    /// Build the user message with the text to check and source context.
    /// </summary>
    private static string BuildUserPrompt(string text, string sourceSentence, LanguageDirection direction)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("[Source sentence]:");
        sb.AppendLine(sourceSentence);
        sb.AppendLine();

        sb.AppendLine("[Translation to check]:");
        sb.AppendLine(text);
        sb.AppendLine();

        if (direction == LanguageDirection.EnToAr)
        {
            sb.AppendLine("Check the Arabic translation for grammar, spelling, punctuation, and style issues.");
        }
        else
        {
            sb.AppendLine("Check the English translation for grammar, spelling, punctuation, and style issues.");
        }

        return sb.ToString();
    }

    // ════════════════════════════════════════════════════════════════
    // Response Parsing
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parse the LLM's structured JSON response into typed GrammarIssue records.
    /// Handles various response formats (raw JSON, markdown-wrapped JSON, partial responses).
    /// Maps approximate line numbers to actual text positions.
    /// </summary>
    private IReadOnlyList<GrammarIssue> ParseGrammarResponse(
        string rawResponse, string originalText, LanguageDirection direction)
    {
        var issues = new List<GrammarIssue>();

        try
        {
            // Extract JSON from the response (handle markdown wrapping)
            var json = ExtractJson(rawResponse);
            if (string.IsNullOrWhiteSpace(json)) return issues;

            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("[Grammar] Response is not a JSON array");
                return issues;
            }

            var lines = originalText.Split('\n');
            var issueId = 0;

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                try
                {
                    var typeStr = element.GetProperty("type").GetString() ?? "grammar";
                    var message = element.GetProperty("message").GetString() ?? "Unknown issue";
                    var suggestion = element.GetProperty("suggestion").GetString() ?? string.Empty;
                    var original = element.GetProperty("original").GetString() ?? string.Empty;
                    var line = element.TryGetProperty("line", out var lineProp) ? lineProp.GetInt32() : 1;

                    // Map the original text fragment to actual position
                    var (startLine, endLine, startCol, endCol) = FindTextPosition(
                        originalText, original, line, lines);

                    // Parse error type
                    var errorType = typeStr.ToLowerInvariant() switch
                    {
                        "spelling" => GrammarErrorType.Spelling,
                        "grammar" => GrammarErrorType.Grammar,
                        "punctuation" => GrammarErrorType.Punctuation,
                        "style" => GrammarErrorType.Style,
                        _ => GrammarErrorType.Grammar
                    };

                    var language = direction == LanguageDirection.EnToAr ? "ar" : "en";

                    issues.Add(new GrammarIssue(
                        Id: $"gram-{++issueId}",
                        Type: errorType,
                        Message: message,
                        Suggestion: suggestion,
                        StartLineNumber: startLine,
                        EndLineNumber: endLine,
                        StartColumn: startCol,
                        EndColumn: endCol,
                        OriginalText: original,
                        Language: language
                    ));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[Grammar] Failed to parse individual issue");
                    // Continue parsing remaining issues
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[Grammar] Failed to parse LLM response as JSON");
        }

        return issues;
    }

    /// <summary>
    /// Extract JSON from the LLM response, handling markdown code blocks.
    /// </summary>
    private static string ExtractJson(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse)) return string.Empty;

        var trimmed = rawResponse.Trim();

        // Handle markdown-wrapped JSON: ```json\n...\n```
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
            {
                trimmed = trimmed[(firstNewline + 1)..];
                var lastBackticks = trimmed.LastIndexOf("```");
                if (lastBackticks >= 0)
                {
                    trimmed = trimmed[..lastBackticks];
                }
            }
        }

        // Find the JSON array start/end
        var arrayStart = trimmed.IndexOf('[');
        var arrayEnd = trimmed.LastIndexOf(']');

        if (arrayStart >= 0 && arrayEnd > arrayStart)
        {
            return trimmed[arrayStart..(arrayEnd + 1)];
        }

        return trimmed;
    }

    /// <summary>
    /// Find the actual position of a text fragment in the original text.
    /// Uses approximate line number from the LLM as a hint, then searches
    /// within that line and nearby lines for the exact match.
    /// </summary>
    private static (int StartLine, int EndLine, int StartCol, int EndCol) FindTextPosition(
        string fullText, string fragment, int hintLine, string[] lines)
    {
        if (string.IsNullOrWhiteSpace(fragment))
            return (hintLine, hintLine, 1, 1);

        // Normalize for comparison
        var normalizedFragment = fragment.Trim();

        // Search in the hint line and nearby lines
        var searchStart = Math.Max(0, hintLine - 2);
        var searchEnd = Math.Min(lines.Length, hintLine + 2);

        for (int i = searchStart; i < searchEnd; i++)
        {
            var line = lines[i];
            var normalizedLine = line.Trim();

            var idx = normalizedLine.IndexOf(normalizedFragment, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                // Adjust column to account for the trim offset
                var trimOffset = line.Length - line.TrimStart().Length;
                var actualStartCol = idx + trimOffset + 1;
                var actualEndCol = idx + trimOffset + normalizedFragment.Length + 1;

                return (i + 1, i + 1, actualStartCol, actualEndCol);
            }
        }

        // Fallback: search the entire text
        var globalIdx = fullText.IndexOf(normalizedFragment, StringComparison.OrdinalIgnoreCase);
        if (globalIdx >= 0)
        {
            // Find the line containing this index
            int lineCount = 1;
            int lineStart = 0;
            for (int i = 0; i < fullText.Length; i++)
            {
                if (i == globalIdx)
                {
                    var startLine = lineCount;
                    var startCol = globalIdx - lineStart + 1;
                    var endCol = globalIdx - lineStart + normalizedFragment.Length + 1;
                    return (startLine, startLine, startCol, endCol);
                }
                if (fullText[i] == '\n')
                {
                    lineCount++;
                    lineStart = i + 1;
                }
            }
        }

        // Final fallback: use the hint line
        return (hintLine, hintLine, 1, Math.Max(1, normalizedFragment.Length + 1));
    }
}
