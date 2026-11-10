using Jellyfin.Plugin.ThunderCat.Utils;

namespace Jellyfin.Plugin.ThunderCat.Tests;

public sealed class MediaCodeExtractorTests
{
    [Theory]
    [InlineData("FC2-PPV-1234567 title", "FC2-PPV-1234567")]
    [InlineData("fc2_ppv_1234567", "FC2-PPV-1234567")]
    [InlineData("LULU-421 1080p", "LULU-421")]
    [InlineData("abc_123 subtitle", "ABC-123")]
    [InlineData("ABC12345", "ABC12345")]
    [InlineData("390JAC-177", "390JAC-177")]
    public void Extract_ReturnsKnownMediaCodes(string title, string expected)
    {
        Assert.Equal(expected, MediaCodeExtractor.Extract(title));
    }

    [Theory]
    [InlineData("Episode EP-01")]
    [InlineData("Series S01E02")]
    [InlineData("Movie 2026 1080p")]
    public void Extract_DoesNotTreatGenericEpisodeMarkersAsCodes(string title)
    {
        Assert.Null(MediaCodeExtractor.Extract(title));
    }
}
