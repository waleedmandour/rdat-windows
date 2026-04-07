using Xunit;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Core.Tests;

public class LanguageDirectionTests
{
    [Fact]
    public void EnToAr_Value_IsDefined()
    {
        var dir = LanguageDirection.EnToAr;
        Assert.Equal(0, (int)dir);
    }

    [Fact]
    public void ArToEn_Value_IsDefined()
    {
        var dir = LanguageDirection.ArToEn;
        Assert.Equal(1, (int)dir);
    }

    [Fact]
    public void LanguagePair_StoresAllFields()
    {
        var pair = new LanguagePair(
            "en", "ar",
            "English", "Arabic",
            "الإنجليزية", "العربية"
        );

        Assert.Equal("en", pair.Source);
        Assert.Equal("ar", pair.Target);
        Assert.Equal("English", pair.SourceLabel);
        Assert.Equal("Arabic", pair.TargetLabel);
        Assert.Equal("الإنجليزية", pair.SourceLabelAr);
        Assert.Equal("العربية", pair.TargetLabelAr);
    }
}
