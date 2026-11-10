using Jellyfin.Plugin.ThunderCat.Infrastructure;

namespace Jellyfin.Plugin.ThunderCat.Tests;

public sealed class AutomaticDownloadGuardTests
{
    [Fact]
    public void SuccessfulDownloadSuppressesDuplicateLanguageAcrossProviders()
    {
        AutomaticDownloadGuard.ResetForTests();
        var subtitleCatKey = AutomaticDownloadGuard.CreateKey(
            @"\\server\media\Movie.strm",
            "chi");
        var thunderKey = AutomaticDownloadGuard.CreateKey(
            @"\\SERVER\MEDIA\Movie.strm",
            "zh-CN");

        Assert.False(AutomaticDownloadGuard.ShouldSuppressSearch(subtitleCatKey));

        AutomaticDownloadGuard.MarkSuccessfulDownload(subtitleCatKey);

        Assert.True(AutomaticDownloadGuard.ShouldSuppressSearch(subtitleCatKey));
        Assert.True(AutomaticDownloadGuard.ShouldSuppressSearch(thunderKey));
        AutomaticDownloadGuard.ResetForTests();
    }

    [Fact]
    public void MissingMediaPathDoesNotCreateDeduplicationKey()
    {
        Assert.Null(AutomaticDownloadGuard.CreateKey(null, "chi"));
        Assert.Null(AutomaticDownloadGuard.CreateKey(" ", "chi"));
    }
}
