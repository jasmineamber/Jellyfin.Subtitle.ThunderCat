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

    [Theory]
    [InlineData("简体", "chi")]
    [InlineData("繁体", "chi")]
    [InlineData("英语", "eng")]
    [InlineData("English", "eng")]
    [InlineData("日语", "jpn")]
    [InlineData("韩语", "kor")]
    [InlineData("chs", "chi")]
    [InlineData("cht", "chi")]
    public void ResolveReportedLanguage_NormalizesThunderLabels(string reported, string expected)
    {
        Assert.Equal(expected, LanguageNormalizer.ResolveReportedLanguage("chi", new[] { reported }));
    }

    [Fact]
    public void ResolveReportedLanguage_PrefersRequestedLanguageForBilingualSubtitle()
    {
        Assert.Equal(
            "eng",
            LanguageNormalizer.ResolveReportedLanguage("eng", new[] { "简体&英语" }));
    }

    [Theory]
    [InlineData("默认")]
    [InlineData("未知语言")]
    [InlineData("SubtitleHandler")]
    [InlineData("2001")]
    [InlineData("DTS-WiKi")]
    [InlineData("srt")]
    public void ResolveReportedLanguage_IgnoresUnknownThunderLabels(string reported)
    {
        Assert.Equal("chi", LanguageNormalizer.ResolveReportedLanguage("chi", new[] { reported }));
    }

    [Fact]
    public void ResolveReportedLanguage_PrefersRequestedLanguageForChineseJapaneseSubtitle()
    {
        Assert.Equal(
            "jpn",
            LanguageNormalizer.ResolveReportedLanguage("jpn", new[] { "简体&日语" }));
    }

    [Theory]
    [InlineData("chi", "简体", true)]
    [InlineData("eng", "简体&英语", true)]
    [InlineData("jpn", "简体&日语", true)]
    [InlineData("chi", "未知语言", false)]
    [InlineData("chi", "DTS-WiKi", false)]
    [InlineData("chi", "英语", false)]
    public void ReportsRequestedLanguage_RequiresRecognizedMatchingMetadata(
        string requested,
        string reported,
        bool expected)
    {
        Assert.Equal(
            expected,
            LanguageNormalizer.ReportsRequestedLanguage(requested, new[] { reported }));
    }

}
