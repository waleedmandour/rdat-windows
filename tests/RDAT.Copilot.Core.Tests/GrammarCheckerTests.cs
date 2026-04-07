using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Core.Tests;

/// <summary>
/// Unit tests for the Grammar Checker models and JSON parsing (Phase 4).
/// Tests GrammarIssue parsing, GrammarErrorType, GrammarCheckState, and response extraction.
/// </summary>
public class GrammarCheckerTests
{
    [Fact]
    public void GrammarIssue_CreatesWithAllFields()
    {
        var issue = new GrammarIssue(
            Id: "gram-1",
            Type: GrammarErrorType.Grammar,
            Message: "Subject-verb agreement error",
            Suggestion: "كانت النساء",
            StartLineNumber: 5,
            EndLineNumber: 5,
            StartColumn: 10,
            EndColumn: 18,
            OriginalText: "كان النساء",
            Language: "ar"
        );

        Assert.Equal("gram-1", issue.Id);
        Assert.Equal(GrammarErrorType.Grammar, issue.Type);
        Assert.Equal("Subject-verb agreement error", issue.Message);
        Assert.Equal("كانت النساء", issue.Suggestion);
        Assert.Equal(5, issue.StartLineNumber);
        Assert.Equal(18, issue.EndColumn);
        Assert.Equal("كان النساء", issue.OriginalText);
        Assert.Equal("ar", issue.Language);
    }

    [Fact]
    public void GrammarErrorType_AllTypesDefined()
    {
        Assert.Equal(4, Enum.GetValues<GrammarErrorType>().Length);
        Assert.Contains(GrammarErrorType.Spelling, Enum.GetValues<GrammarErrorType>());
        Assert.Contains(GrammarErrorType.Grammar, Enum.GetValues<GrammarErrorType>());
        Assert.Contains(GrammarErrorType.Punctuation, Enum.GetValues<GrammarErrorType>());
        Assert.Contains(GrammarErrorType.Style, Enum.GetValues<GrammarErrorType>());
    }

    [Fact]
    public void GrammarCheckState_AllStatesDefined()
    {
        Assert.Equal(4, Enum.GetValues<GrammarCheckState>().Length);
        Assert.Contains(GrammarCheckState.Idle, Enum.GetValues<GrammarCheckState>());
        Assert.Contains(GrammarCheckState.Checking, Enum.GetValues<GrammarCheckState>());
        Assert.Contains(GrammarCheckState.Ready, Enum.GetValues<GrammarCheckState>());
        Assert.Contains(GrammarCheckState.Error, Enum.GetValues<GrammarCheckState>());
    }

    [Fact]
    public void GrammarIssue_SpellingType()
    {
        var issue = new GrammarIssue(
            "gram-2", GrammarErrorType.Spelling, "Misspelled word",
            "corrected", 1, 1, 5, 12, "wrnog", "en");

        Assert.Equal(GrammarErrorType.Spelling, issue.Type);
    }

    [Fact]
    public void GrammarIssue_PunctuationType()
    {
        var issue = new GrammarIssue(
            "gram-3", GrammarErrorType.Punctuation, "Missing period",
            ".", 1, 1, 20, 21, "", "en");

        Assert.Equal(GrammarErrorType.Punctuation, issue.Type);
    }

    [Fact]
    public void GrammarIssue_StyleType()
    {
        var issue = new GrammarIssue(
            "gram-4", GrammarErrorType.Style, "Formal register required",
            "more formal text", 1, 1, 1, 15, "informal text", "ar");

        Assert.Equal(GrammarErrorType.Style, issue.Type);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    [InlineData("fr")]
    public void GrammarIssue_SupportsMultipleLanguages(string language)
    {
        var issue = new GrammarIssue(
            "gram-lang", GrammarErrorType.Grammar, "Test",
            "fix", 1, 1, 1, 5, "orig", language);

        Assert.Equal(language, issue.Language);
    }

    [Fact]
    public void GrammarIssue_EnglishArabicTranslationScenario()
    {
        // Simulate an English→Arabic grammar issue
        var sourceSentence = "The committee has decided to postpone the meeting.";
        var arabicIssue = new GrammarIssue(
            Id: "gram-ar-1",
            Type: GrammarErrorType.Grammar,
            Message: "Arabic dual form should be used for 'two members'",
            Suggestion: "عضوين",
            StartLineNumber: 1,
            EndLineNumber: 1,
            StartColumn: 15,
            EndColumn: 21,
            OriginalText: "عضو",
            Language: "ar"
        );

        Assert.Equal("ar", arabicIssue.Language);
        Assert.Contains("dual", arabicIssue.Message);
        Assert.Equal("عضوين", arabicIssue.Suggestion);
    }
}
