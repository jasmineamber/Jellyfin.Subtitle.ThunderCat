using Jellyfin.Plugin.ThunderCat.Utils;

namespace Jellyfin.Plugin.ThunderCat.Tests;

public sealed class LanguageNormalizerTests
{
    [Theory]
    [InlineData("zh-Hans", "chi")]
    [InlineData("zho", "chi")]
    [InlineData("en", "eng")]
    [InlineData("deu", "ger")]
    public void NormalizeForJellyfin_UsesStableThreeLetterCodes(string input, string expected)
    {
        Assert.Equal(expected, LanguageNormalizer.NormalizeForJellyfin(input));
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

    [Fact]
    public void GetSubtitleCatLanguagePreferences_DefaultChinesePrefersSimplified()
    {
        var preferences = LanguageNormalizer.GetSubtitleCatLanguagePreferences("chi");

        Assert.Equal(
            new[]
            {
                "zh-CN",
                "zh-Hans",
                "zh",
                "chi",
                "zho",
                "zh-TW",
                "zh-Hant",
                "zh-HK"
            },
            preferences);
    }

    [Fact]
    public void GetSubtitleCatLanguagePreferences_ExplicitTraditionalKeepsTraditionalFirst()
    {
        var preferences = LanguageNormalizer.GetSubtitleCatLanguagePreferences("zh-TW");

        Assert.Equal("zh-TW", preferences[0]);
        Assert.True(
            preferences.ToList().IndexOf("zh-TW") <
            preferences.ToList().IndexOf("zh-CN"));
    }
}
