using System.Net.Http;
using Jellyfin.Plugin.ThunderCat.Infrastructure;

namespace Jellyfin.Plugin.ThunderCat.Tests;

public sealed class SafeHttpClientTests
{
    [Fact]
    public async Task ExecuteWithRetryAsync_RetriesWithFreshTimeoutAfterFirstAttemptExpires()
    {
        var attempts = 0;

        var result = await SafeHttpClient.ExecuteWithRetryAsync(
            async token =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }

                return "ok";
            },
            TimeSpan.FromMilliseconds(30),
            2,
            CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_PropagatesCallerCancellationWithoutRetrying()
    {
        var attempts = 0;
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SafeHttpClient.ExecuteWithRetryAsync(
                async token =>
                {
                    Interlocked.Increment(ref attempts);
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return "unreachable";
                },
                TimeSpan.FromSeconds(2),
                2,
                source.Token));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_CallerCancellationInterruptsRetryDelay()
    {
        var attempts = 0;
        using var source = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SafeHttpClient.ExecuteWithRetryAsync<string>(
                _ =>
                {
                    Interlocked.Increment(ref attempts);
                    source.Cancel();
                    throw new HttpRequestException("Transient failure.");
                },
                TimeSpan.FromSeconds(2),
                2,
                source.Token));

        Assert.Equal(1, attempts);
    }
}
