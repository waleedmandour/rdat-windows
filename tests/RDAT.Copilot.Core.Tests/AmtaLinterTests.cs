using RDAT.Copilot.Core.Models;
using Xunit;

namespace RDAT.Copilot.Core.Tests;

/// <summary>
/// Unit tests for the AMTA Linter models (Phase 4).
/// Tests GlossaryTerm, AmtaLintIssue, AmtaLintType, and AmtaLintSeverity.
/// </summary>
public class AmtaLinterModelsTests
{
    [Fact]
    public void GlossaryTerm_CreatesWithRequiredFields()
    {
        var term = new GlossaryTerm(
            Id: "g-1",
            SourceTerm: "climate change",
            TargetTerm: "تغير المناخ"
        );

        Assert.Equal("g-1", term.Id);
        Assert.Equal("climate change", term.SourceTerm);
        Assert.Equal("تغير المناخ", term.TargetTerm);
        Assert.Null(term.Domain);
        Assert.Null(term.Notes);
        Assert.True(term.IsRequired);
        Assert.Null(term.PartOfSpeech);
    }

    [Fact]
    public void GlossaryTerm_CreatesWithAllFields()
    {
        var term = new GlossaryTerm(
            Id: "g-2",
            SourceTerm: "sustainable development",
            TargetTerm: "التنمية المستدامة",
            Domain: "Environment",
            Notes: "Official UN translation",
            IsRequired: true,
            PartOfSpeech: "noun"
        );

        Assert.Equal("g-2", term.Id);
        Assert.Equal("sustainable development", term.SourceTerm);
        Assert.Equal("التنمية المستدامة", term.TargetTerm);
        Assert.Equal("Environment", term.Domain);
        Assert.Equal("Official UN translation", term.Notes);
        Assert.True(term.IsRequired);
        Assert.Equal("noun", term.PartOfSpeech);
    }

    [Fact]
    public void AmtaLintIssue_CreatesWithAllFields()
    {
        var term = new GlossaryTerm("g-1", "climate", "المناخ", "Environment");
        var issue = new AmtaLintIssue(
            Id: "amt-1",
            Type: AmtaLintType.MissingTerm,
            Severity: AmtaLintSeverity.Error,
            Message: "Required term \"climate\" → \"المناخ\" not found",
            Suggestion: "المناخ",
            OriginalText: "climate",
            StartLineNumber: 3,
            EndLineNumber: 3,
            StartColumn: 10,
            EndColumn: 17,
            MatchingTerm: term,
            Domain: "Environment"
        );

        Assert.Equal("amt-1", issue.Id);
        Assert.Equal(AmtaLintType.MissingTerm, issue.Type);
        Assert.Equal(AmtaLintSeverity.Error, issue.Severity);
        Assert.Equal("المناخ", issue.Suggestion);
        Assert.Equal("climate", issue.OriginalText);
        Assert.Equal(3, issue.StartLineNumber);
        Assert.Equal(17, issue.EndColumn);
        Assert.Equal(term, issue.MatchingTerm);
        Assert.Equal("Environment", issue.Domain);
    }

    [Fact]
    public void AmtaLintType_AllValuesDefined()
    {
        Assert.Equal(5, Enum.GetValues<AmtaLintType>().Length);
        Assert.Contains(AmtaLintType.MissingTerm, Enum.GetValues<AmtaLintType>());
        Assert.Contains(AmtaLintType.InconsistentTerm, Enum.GetValues<AmtaLintType>());
        Assert.Contains(AmtaLintType.UntranslatedTerm, Enum.GetValues<AmtaLintType>());
        Assert.Contains(AmtaLintType.MistranslatedTerm, Enum.GetValues<AmtaLintType>());
        Assert.Contains(AmtaLintType.CasingIssue, Enum.GetValues<AmtaLintType>());
    }

    [Fact]
    public void AmtaLintSeverity_AllValuesDefined()
    {
        Assert.Equal(3, Enum.GetValues<AmtaLintSeverity>().Length);
        Assert.Contains(AmtaLintSeverity.Info, Enum.GetValues<AmtaLintSeverity>());
        Assert.Contains(AmtaLintSeverity.Warning, Enum.GetValues<AmtaLintSeverity>());
        Assert.Contains(AmtaLintSeverity.Error, Enum.GetValues<AmtaLintSeverity>());
    }

    [Fact]
    public void AmtaLinterState_AllValuesDefined()
    {
        Assert.Equal(5, Enum.GetValues<AmtaLinterState>().Length);
        Assert.Contains(AmtaLinterState.Idle, Enum.GetValues<AmtaLinterState>());
        Assert.Contains(AmtaLinterState.Loading, Enum.GetValues<AmtaLinterState>());
        Assert.Contains(AmtaLinterState.Ready, Enum.GetValues<AmtaLinterState>());
        Assert.Contains(AmtaLinterState.Checking, Enum.GetValues<AmtaLinterState>());
        Assert.Contains(AmtaLinterState.Error, Enum.GetValues<AmtaLinterState>());
    }
}
