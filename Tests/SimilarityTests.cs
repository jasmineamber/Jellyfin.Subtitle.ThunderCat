using Emby.Subtitle.ThunderCat.Utils;

namespace Emby.Subtitle.ThunderCat.Tests;

public sealed class SimilarityTests
{
    [Fact]
    public void GetTitleSimilarity_NormalizesFilenameSeparatorsAndReleaseNoise()
    {
        var close = Similarity.GetTitleSimilarity(
            "Test.Movie.2026.1080p.mkv",
            "Test Movie 2026");
        var unrelated = Similarity.GetTitleSimilarity(
            "Test.Movie.2026.1080p.mkv",
            "Different Series S01E02");

        Assert.True(close > unrelated);
        Assert.True(close > 0.5d);
    }

    [Fact]
    public void GetTitleSimilarity_DoesNotTreatAnagramsAsPerfectMatches()
    {
        var score = Similarity.GetTitleSimilarity("abc", "cba");

        Assert.True(score < 0.5d);
    }

    [Fact]
    public void GetTitleSimilarity_ReturnsOneForEquivalentNormalizedTitles()
    {
        Assert.Equal(1d, Similarity.GetTitleSimilarity("Movie.Title", "movie title"));
    }
}
