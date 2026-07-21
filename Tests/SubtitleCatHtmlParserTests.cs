using Emby.Subtitle.ThunderCat.Utils;

namespace Emby.Subtitle.ThunderCat.Tests;

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
        Assert.EndsWith("-zh-CN.srt", chinese.Uri.AbsolutePath, StringComparison.Ordinal);

        var english = SubtitleCatHtmlParser.FindDownloadLink(html, baseUri, "eng");
        Assert.NotNull(english);
        Assert.Equal("eng", english!.Language);

        Assert.Null(SubtitleCatHtmlParser.FindDownloadLink(html, baseUri, "fr"));
    }

    private static string ReadFixture(params string[] parts)
    {
        var path = Path.Combine(new[] { AppContext.BaseDirectory, "Fixtures" }.Concat(parts).ToArray());
        return File.ReadAllText(path);
    }
}
