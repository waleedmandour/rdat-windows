using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Core.Interfaces;

/// <summary>
/// Contract for the AI Grammar/Spell Checker service (Phase 4).
/// Uses the local LLM to detect spelling, grammar, punctuation, and style issues.
/// </summary>
public interface IGrammarCheckerService
{
    /// <summary>Current grammar check state.</summary>
    GrammarCheckState State { get; }

    /// <summary>
    /// Run grammar/spell check on the given text.
    /// Returns a list of detected issues with positions.
    /// </summary>
    Task<IReadOnlyList<GrammarIssue>> CheckAsync(
        string text,
        string sourceSentence,
        LanguageDirection languageDirection,
        CancellationToken cancellationToken = default);
}
