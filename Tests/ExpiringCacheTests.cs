using Jellyfin.Plugin.ThunderCat.Infrastructure;

namespace Jellyfin.Plugin.ThunderCat.Tests;

public sealed class ExpiringCacheTests
{
    [Fact]
    public void TryGet_ReturnsValueBeforeExpiry()
    {
        var now = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);
        var cache = new ExpiringCache<string>(TimeSpan.FromMinutes(5), 2, clock: () => now);
        cache.Set("key", "value");

        Assert.True(cache.TryGet("key", out var value));
        Assert.Equal("value", value);
    }

    [Fact]
    public void TryGet_RemovesExpiredValue()
    {
        var now = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);
        var cache = new ExpiringCache<string>(TimeSpan.FromMinutes(5), 2, clock: () => now);
        cache.Set("key", "value");

        now = now.AddMinutes(5);

        Assert.False(cache.TryGet("key", out _));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Set_EvictsOldestEntryAtCapacity()
    {
        var now = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);
        var cache = new ExpiringCache<string>(TimeSpan.FromHours(1), 2, clock: () => now);
        cache.Set("oldest", "one");
        now = now.AddSeconds(1);
        cache.Set("newer", "two");
        now = now.AddSeconds(1);
        cache.Set("newest", "three");

        Assert.False(cache.TryGet("oldest", out _));
        Assert.True(cache.TryGet("newer", out _));
        Assert.True(cache.TryGet("newest", out _));
    }

    [Fact]
    public void Cache_UsesConfiguredKeyComparer()
    {
        var cache = new ExpiringCache<string>(
            TimeSpan.FromMinutes(5),
            2,
            StringComparer.OrdinalIgnoreCase);
        cache.Set("Movie", "value");

        Assert.True(cache.TryGet("movie", out var value));
        Assert.Equal("value", value);
    }
}
