using Xunit;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Core.Tests;

public class GrammarIssueTests
{
    [Fact]
    public void GrammarIssue_StoresAllFields()
    {
        var issue = new GrammarIssue(
            Id: "grammar-1-5",
            Type: GrammarErrorType.Spelling,
            Message: "Misspelled word",
            Suggestion: "correct",
            StartLineNumber: 5,
            EndLineNumber: 5,
            StartColumn: 10,
            EndColumn: 17,
            OriginalText: "incorrct",
            Language: "en"
        );

        Assert.Equal("grammar-1-5", issue.Id);
        Assert.Equal(GrammarErrorType.Spelling, issue.Type);
        Assert.Equal(5, issue.StartLineNumber);
        Assert.Equal("incorrct", issue.OriginalText);
    }
}
