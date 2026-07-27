using Emby.Subtitle.ThunderCat.Utils;

namespace Emby.Subtitle.ThunderCat.Tests;

public sealed class SearchQueryBuilderTests
{
    [Fact]
    public void Create_UsesMetadataNameForRanking()
    {
        var plan = SearchQueryBuilder.Create("  Movie Title  ", "/media/fallback.mkv");

        Assert.Equal("Movie Title", plan.RankingTitle);
    }

    [Fact]
    public void Create_FallsBackToMediaFilenameForRanking()
    {
        var plan = SearchQueryBuilder.Create(null, "/media/Movie.Title.2026.mkv");

        Assert.Equal("Movie.Title.2026", plan.RankingTitle);
        Assert.Equal(new[] { "Movie.Title.2026" }, plan.Queries);
    }

    [Fact]
    public void Create_PutsFilenameCodeBeforeMetadataTitle()
    {
        var plan = SearchQueryBuilder.Create(
            "Friendly Movie Title",
            "/media/LULU-421.1080p.mkv");

        Assert.Equal("LULU-421", plan.MediaCode);
        Assert.Equal(
            new[] { "LULU-421", "Friendly Movie Title" },
            plan.Queries);
    }

    [Fact]
    public void Create_UsesFilenameAsFallbackWhenNoCodeExists()
    {
        var plan = SearchQueryBuilder.Create(
            "Localized Movie Title",
            "/media/Original.Movie.Title.2026.mkv");

        Assert.Equal(
            new[] { "Localized Movie Title", "Original.Movie.Title.2026" },
            plan.Queries);
    }

    [Fact]
    public void Create_DoesNotDuplicateExactCode()
    {
        var plan = SearchQueryBuilder.Create("LULU-421", "/media/LULU-421.mkv");

        Assert.Equal(new[] { "LULU-421" }, plan.Queries);
    }

    [Fact]
    public void Create_ReturnsEmptyQueriesForBlankInput()
    {
        Assert.Empty(SearchQueryBuilder.Create("  ", null).Queries);
    }

    [Fact]
    public void Create_LimitsOversizedQueryWithoutSplittingSurrogatePair()
    {
        var title = new string('A', 239) + "😀" + new string('B', 20);

        var query = Assert.Single(SearchQueryBuilder.Create(title, null).Queries);

        Assert.Equal(239, query.Length);
        Assert.DoesNotContain("😀", query, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveFileName_ReturnsEmptyForInvalidPath()
    {
        Assert.Equal(string.Empty, SearchQueryBuilder.ResolveFileName("invalid\0path.mkv"));
    }
}
