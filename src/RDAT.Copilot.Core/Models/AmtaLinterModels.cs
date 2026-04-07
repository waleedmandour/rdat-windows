namespace RDAT.Copilot.Core.Models;

/// <summary>
/// Severity levels for AMTA terminology lint issues.
/// Determines how issues are displayed in the editor and error list.
/// </summary>
public enum AmtaLintSeverity
{
    /// <summary>Suggestion — recommended but not required terminology change.</summary>
    Info,

    /// <summary>Warning — term is used but may not match approved glossary.</summary>
    Warning,

    /// <summary>Error — required term is missing or incorrect.</summary>
    Error
}

/// <summary>
/// Types of terminology issues detected by the AMTA linter.
/// </summary>
public enum AmtaLintType
{
    /// <summary>An approved term from the glossary was not used in the translation.</summary>
    MissingTerm,

    /// <summary>A term was translated differently from the approved glossary entry.</summary>
    InconsistentTerm,

    /// <summary>A term in the source was left untranslated in the target.</summary>
    UntranslatedTerm,

    /// <summary>A source term appears to be mistranslated based on glossary.</summary>
    MistranslatedTerm,

    /// <summary>A capitalized/abbreviated term (e.g., WHO, UNESCO) was incorrectly translated.</summary>
    CasingIssue
}

/// <summary>
/// State of the AMTA terminology linter.
/// </summary>
public enum AmtaLinterState
{
    /// <summary>No glossary loaded.</summary>
    Idle,

    /// <summary>Glossary file is being loaded/parsed.</summary>
    Loading,

    /// <summary>Glossary loaded, linter ready for checks.</summary>
    Ready,

    /// <summary>Terminology check is running.</summary>
    Checking,

    /// <summary>An error occurred during loading or checking.</summary>
    Error
}

/// <summary>
/// A terminology entry in the AMTA glossary.
/// Represents an approved source→target term pair with optional metadata.
/// </summary>
/// <param name="Id">Unique identifier for the glossary entry.</param>
/// <param name="SourceTerm">Source-language term (e.g., "climate change").</param>
/// <param name="TargetTerm">Approved target-language translation (e.g., "تغير المناخ").</param>
/// <param name="Domain">Optional domain/category (e.g., "Environment", "Legal").</param>
/// <param name="Notes">Optional usage notes or context restrictions.</param>
/// <param name="IsRequired">Whether this term MUST be used (true) or is preferred (false).</param>
/// <param name="PartOfSpeech">Optional grammatical part of speech for disambiguation.</param>
public record GlossaryTerm(
    string Id,
    string SourceTerm,
    string TargetTerm,
    string? Domain = null,
    string? Notes = null,
    bool IsRequired = true,
    string? PartOfSpeech = null
);

/// <summary>
/// A terminology issue detected by the AMTA linter.
/// Carries position information for Monaco marker placement and
/// includes the suggested correction from the glossary.
/// </summary>
/// <param name="Id">Unique issue identifier.</param>
/// <param name="Type">Type of terminology issue.</param>
/// <param name="Severity">Issue severity level.</param>
/// <param name="Message">Human-readable description of the issue.</param>
/// <param name="Suggestion">Suggested replacement text from the glossary.</param>
/// <param name="OriginalText">The text that triggered the issue.</param>
/// <param name="StartLineNumber">1-based start line number.</param>
/// <param name="EndLineNumber">1-based end line number.</param>
/// <param name="StartColumn">1-based start column.</param>
/// <param name="EndColumn">1-based end column.</param>
/// <param name="MatchingTerm">The glossary term that was violated.</param>
/// <param name="Domain">Domain of the glossary term.</param>
public record AmtaLintIssue(
    string Id,
    AmtaLintType Type,
    AmtaLintSeverity Severity,
    string Message,
    string Suggestion,
    string OriginalText,
    int StartLineNumber,
    int EndLineNumber,
    int StartColumn,
    int EndColumn,
    GlossaryTerm MatchingTerm,
    string? Domain = null
);
