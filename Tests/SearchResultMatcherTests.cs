using Emby.Subtitle.ThunderCat.Utils;

namespace Emby.Subtitle.ThunderCat.Tests;

public sealed class SearchResultMatcherTests
{
    [Fact]
    public void Evaluate_PrioritizesCidAboveOtherSignals()
    {
        var cid = SearchResultMatcher.Evaluate("LULU-421", "Unrelated subtitle", "LULU-421", true);
        var code = SearchResultMatcher.Evaluate("LULU-421", "LULU-421 subtitle", "LULU-421", false);

        Assert.Equal(SearchMatchKind.Cid, cid.Kind);
        Assert.True(cid.IsEligible);
        Assert.True(cid.Rank > code.Rank);
    }

    [Fact]
    public void Evaluate_DetectsMatchingMediaCode()
    {
        var match = SearchResultMatcher.Evaluate(
            "LULU-421 1080p",
            "LULU-421 Chinese.srt",
            "LULU-421",
            false);

        Assert.Equal(SearchMatchKind.MediaCode, match.Kind);
        Assert.True(match.IsEligible);
        Assert.True(SearchResultMatcher.IsConfident(match.Kind));
    }

    [Theory]
    [InlineData("HMN-878", "HMN-007", "HMN-878")]
    [InlineData("ABC-123", "ABC-124 subtitle", "ABC-123")]
    public void Evaluate_RejectsConflictingMediaCodes(
        string queryTitle,
        string candidateTitle,
        string queryCode)
    {
        var match = SearchResultMatcher.Evaluate(queryTitle, candidateTitle, queryCode, false);

        Assert.Equal(SearchMatchKind.Weak, match.Kind);
        Assert.True(match.HasIdentifierConflict);
        Assert.False(match.IsEligible);
    }

    [Fact]
    public void Evaluate_AcceptsEquivalentMediaCodeWithDifferentSeparator()
    {
        var match = SearchResultMatcher.Evaluate(
            "ABC-123",
            "abc_123 subtitle",
            "ABC-123",
            false);

        Assert.Equal(SearchMatchKind.MediaCode, match.Kind);
        Assert.True(match.IsEligible);
    }

    [Fact]
    public void Evaluate_AcceptsEquivalentCompactAndSeparatedMediaCode()
    {
        var match = SearchResultMatcher.Evaluate(
            "ABC12345",
            "ABC-12345 subtitle",
            "ABC12345",
            false);

        Assert.Equal(SearchMatchKind.MediaCode, match.Kind);
        Assert.True(match.IsEligible);
    }

    [Fact]
    public void Evaluate_CidMatchOverridesIdentifierConflict()
    {
        var match = SearchResultMatcher.Evaluate(
            "ABC-123",
            "ABC-124 subtitle",
            "ABC-123",
            true);

        Assert.Equal(SearchMatchKind.Cid, match.Kind);
        Assert.True(match.IsEligible);
        Assert.False(match.HasIdentifierConflict);
    }

    [Fact]
    public void Evaluate_DetectsEquivalentNormalizedTitle()
    {
        var match = SearchResultMatcher.Evaluate(
            "Movie.Title.2026",
            "movie title 2026",
            null,
            false);

        Assert.Equal(SearchMatchKind.ExactTitle, match.Kind);
        Assert.True(match.IsEligible);
    }

    [Fact]
    public void Evaluate_AcceptsSimilarOrdinaryMovieTitle()
    {
        var match = SearchResultMatcher.Evaluate(
            "Movie.Title.2026.1080p.mkv",
            "Movie Title 2026",
            null,
            false);

        Assert.Equal(SearchMatchKind.Title, match.Kind);
        Assert.True(match.IsEligible);
    }

    [Fact]
    public void Evaluate_LeavesUnrelatedTitleWeak()
    {
        var match = SearchResultMatcher.Evaluate(
            "Movie Title 2026",
            "Different Series S01E02",
            null,
            false);

        Assert.Equal(SearchMatchKind.Weak, match.Kind);
        Assert.False(match.IsEligible);
        Assert.False(SearchResultMatcher.IsConfident(match.Kind));
    }

    [Fact]
    public void IsSufficientForQuery_RequiresIdentifierBeforeStoppingCodeFallback()
    {
        var titleOnly = SearchResultMatcher.Evaluate(
            "Friendly Movie Title",
            "Friendly Movie Title Chinese subtitle",
            "ABC-123",
            false);

        Assert.True(titleOnly.IsEligible);
        Assert.False(SearchResultMatcher.IsSufficientForQuery("ABC-123", "ABC-123", titleOnly));
        Assert.True(SearchResultMatcher.IsSufficientForQuery(
            "Friendly Movie Title",
            "ABC-123",
            titleOnly));
    }

    [Theory]
    [InlineData((int)SearchMatchKind.Cid, "[CID] subtitle.srt")]
    [InlineData((int)SearchMatchKind.MediaCode, "[ID] subtitle.srt")]
    [InlineData((int)SearchMatchKind.ExactTitle, "[TITLE] subtitle.srt")]
    [InlineData((int)SearchMatchKind.Title, "subtitle.srt")]
    public void FormatDisplayName_UsesCompactMatchLabels(int kind, string expected)
    {
        Assert.Equal(
            expected,
            SearchResultMatcher.FormatDisplayName("subtitle.srt", (SearchMatchKind)kind));
    }
}
