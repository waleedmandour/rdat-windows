namespace RDAT.Copilot.Core.Models;

/// <summary>
/// Types of grammar/spell errors detected by the AI checker.
/// </summary>
public enum GrammarErrorType
{
    Spelling,
    Grammar,
    Punctuation,
    Style
}

/// <summary>
/// A grammar or spelling issue detected in the target translation text.
/// </summary>
public record GrammarIssue(
    string Id,
    GrammarErrorType Type,
    string Message,
    string Suggestion,
    int StartLineNumber,
    int EndLineNumber,
    int StartColumn,
    int EndColumn,
    string OriginalText,
    string Language
);

/// <summary>
/// State of the grammar checker.
/// </summary>
public enum GrammarCheckState
{
    Idle,
    Checking,
    Ready,
    Error
}
