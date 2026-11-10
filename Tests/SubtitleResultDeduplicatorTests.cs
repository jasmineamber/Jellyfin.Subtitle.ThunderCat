using Jellyfin.Plugin.ThunderCat.Utils;

namespace Jellyfin.Plugin.ThunderCat.Tests;

public sealed class SubtitleResultDeduplicatorTests
{
    [Fact]
    public void TryAdd_RejectsRepeatedUri()
    {
        var deduplicator = new SubtitleResultDeduplicator();
        var uri = new Uri("https://subtitle.example/subtitle.srt?id=AbC");

        Assert.True(deduplicator.TryAdd(uri));
        Assert.False(deduplicator.TryAdd(uri));
    }

    [Fact]
    public void TryAdd_PreservesCaseSensitivePathAndQueryValues()
    {
        var deduplicator = new SubtitleResultDeduplicator();

        Assert.True(deduplicator.TryAdd(new Uri("https://subtitle.example/Sub.srt?id=AbC")));
        Assert.True(deduplicator.TryAdd(new Uri("https://subtitle.example/sub.srt?id=aBc")));
    }

    [Fact]
    public void TryAdd_PreservesDifferentUrlsForSameMediaCid()
    {
        var deduplicator = new SubtitleResultDeduplicator();

        Assert.True(deduplicator.TryAdd(new Uri("https://subtitle.example/one.srt")));
        Assert.True(deduplicator.TryAdd(new Uri("https://subtitle.example/two.srt")));
    }
}
