using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Core.Interfaces;

/// <summary>
/// Contract for the AMTA Terminology Linter service (Phase 4).
/// Validates translation terminology against approved glossaries.
/// Flags missing, inconsistent, or incorrect term usage with severity levels.
///
/// Terminology checking follows AMTA (Association for Machine Translation in the Americas)
/// best practices for quality assurance in translation workflows.
/// </summary>
public interface IAmtaLinterService
{
    /// <summary>Current linter state.</summary>
    AmtaLinterState State { get; }

    /// <summary>Number of terms loaded in the active glossary.</summary>
    int TermCount { get; }

    /// <summary>Whether the linter has a loaded glossary and is ready.</summary>
    bool IsReady { get; }

    /// <summary>
    /// Load a terminology glossary from a CSV or JSON file.
    /// CSV format: source_term, target_term, domain (optional), notes (optional)
    /// JSON format: array of { source, target, domain?, notes? }
    /// </summary>
    /// <param name="filePath">Path to the glossary file.</param>
    /// <param name="progress">Progress reporter for loading.</param>
    /// <returns>Number of terms loaded.</returns>
    Task<int> LoadGlossaryAsync(
        string filePath,
        IProgress<(double Progress, string Text)>? progress = null);

    /// <summary>
    /// Load a terminology glossary from an in-memory collection of term pairs.
    /// Useful for programmatic glossary construction or testing.
    /// </summary>
    /// <param name="terms">Collection of terminology entries.</param>
    void LoadGlossary(IReadOnlyList<GlossaryTerm> terms);

    /// <summary>
    /// Run terminology check on the given target text against the source text.
    /// Identifies terms from the source that are missing or incorrectly translated
    /// in the target, and flags terminology inconsistencies.
    /// </summary>
    /// <param name="targetText">Full target translation text.</param>
    /// <param name="sourceText">Full source text for term extraction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of terminology lint issues.</returns>
    Task<IReadOnlyList<AmtaLintIssue>> CheckAsync(
        string targetText,
        string sourceText,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the list of approved terms in the current glossary.
    /// </summary>
    IReadOnlyList<GlossaryTerm> GetTerms();

    /// <summary>
    /// Clear the loaded glossary and reset state.
    /// </summary>
    void ClearGlossary();

    /// <summary>
    /// Raised when terminology lint results are ready.
    /// </summary>
    event EventHandler<IReadOnlyList<AmtaLintIssue>>? LintCompleted;
}
