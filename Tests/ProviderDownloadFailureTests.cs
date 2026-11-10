using Jellyfin.Plugin.ThunderCat.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.ThunderCat.Tests;

public sealed class ProviderDownloadFailureTests
{
    [Fact]
    public async Task SubtitleCat_InvalidTokenThrowsMeaningfulException()
    {
        var provider = new SubtitleCatProvider(NullLogger<SubtitleCatProvider>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetSubtitles("invalid", CancellationToken.None));

        Assert.Contains("invalid or has expired", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Thunder_InvalidTokenThrowsMeaningfulException()
    {
        var provider = new ThunderProvider(NullLogger<ThunderProvider>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetSubtitles("invalid", CancellationToken.None));

        Assert.Contains("invalid or has expired", exception.Message, StringComparison.Ordinal);
    }
}
