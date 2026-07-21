using Emby.Subtitle.ThunderCat.Utils;

namespace Emby.Subtitle.ThunderCat.Tests;

public sealed class LanguageNormalizerTests
{
    [Theory]
    [InlineData("zh-Hans", "chi")]
    [InlineData("zho", "chi")]
    [InlineData("en", "eng")]
    [InlineData("deu", "ger")]
    public void NormalizeForEmby_UsesStableThreeLetterCodes(string input, string expected)
    {
        Assert.Equal(expected, LanguageNormalizer.NormalizeForEmby(input));
    }

    [Fact]
    public void ResolveReportedLanguage_PrefersUpstreamLanguage()
    {
        Assert.Equal("eng", LanguageNormalizer.ResolveReportedLanguage("chi", new[] { "", "en" }));
    }

    [Fact]
    public void ResolveReportedLanguage_FallsBackToRequestedLanguage()
    {
        Assert.Equal("chi", LanguageNormalizer.ResolveReportedLanguage("zh-CN", new[] { "" }));
    }
}
