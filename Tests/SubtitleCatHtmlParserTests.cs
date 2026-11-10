using Jellyfin.Plugin.ThunderCat.Utils;

namespace Jellyfin.Plugin.ThunderCat.Tests;

public sealed class SubtitleCatHtmlParserTests
{
    [Fact]
    public void ParseSearchResults_HandlesNestedTagsAndAttributeOrder()
    {
        var html = ReadFixture("SubtitleCat", "search.html");
        var items = SubtitleCatHtmlParser.ParseSearchResults(html, new Uri("https://subtitlecat.com/"), 10);

        var item = Assert.Single(items);
        Assert.Equal("Test Movie 2026", item.Title);
        Assert.Equal("https://subtitlecat.com/subs/100/Test.Movie.2026.html", item.DetailUri.AbsoluteUri);
    }

    [Fact]
    public void FindDownloadLink_SelectsRequestedLanguageWithoutAnyLanguageFallback()
    {
        var html = ReadFixture("SubtitleCat", "detail.html");
        var baseUri = new Uri("https://subtitlecat.com/subs/100/Test.Movie.2026.html");

        var chinese = SubtitleCatHtmlParser.FindDownloadLink(html, baseUri, "zh-Hans");
        Assert.NotNull(chinese);
        Assert.Equal("chi", chinese!.Language);
        Assert.Equal("zh-CN", chinese.Variant);
        Assert.EndsWith("-zh-CN.srt", chinese.Uri.AbsolutePath, StringComparison.Ordinal);

        var english = SubtitleCatHtmlParser.FindDownloadLink(html, baseUri, "eng");
        Assert.NotNull(english);
        Assert.Equal("eng", english!.Language);

        Assert.Null(SubtitleCatHtmlParser.FindDownloadLink(html, baseUri, "fr"));
    }

    [Fact]
    public void FindDownloadLinks_ReturnsFallbackVariantsInPreferenceOrder()
    {
        const string html = """
            <a id="download_zh-TW" href="/subs/102/Test-zh-TW.srt">Traditional</a>
            <a id="download_zh-CN" href="/subs/101/Test-zh-CN.srt">Simplified</a>
            """;
        var baseUri = new Uri("https://subtitlecat.com/subs/100/Test.html");

        var links = SubtitleCatHtmlParser.FindDownloadLinks(html, baseUri, "chi");

        Assert.Collection(
            links,
            link =>
            {
                Assert.Equal("chi", link.Language);
                Assert.Equal("zh-CN", link.Variant);
                Assert.EndsWith("-zh-CN.srt", link.Uri.AbsolutePath, StringComparison.Ordinal);
            },
            link =>
            {
                Assert.Equal("chi", link.Language);
                Assert.Equal("zh-TW", link.Variant);
                Assert.EndsWith("-zh-TW.srt", link.Uri.AbsolutePath, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void FindDownloadLinkForVariant_SelectsExactLanguageVariant()
    {
        const string html = """
            <a id="download_zh-TW" href="/subs/102/Test-zh-TW.srt">Traditional</a>
            <a id="download_zh-CN" href="/subs/101/Test-zh-CN.srt">Simplified</a>
            """;
        var baseUri = new Uri("https://subtitlecat.com/subs/100/Test.html");

        var link = SubtitleCatHtmlParser.FindDownloadLinkForVariant(html, baseUri, "zh-CN");

        Assert.NotNull(link);
        Assert.Equal("zh-CN", link!.Variant);
        Assert.EndsWith("-zh-CN.srt", link.Uri.AbsolutePath, StringComparison.Ordinal);
    }

    private static string ReadFixture(params string[] parts)
    {
        var path = Path.Combine(new[] { AppContext.BaseDirectory, "Fixtures" }.Concat(parts).ToArray());
        return File.ReadAllText(path);
    }
}
