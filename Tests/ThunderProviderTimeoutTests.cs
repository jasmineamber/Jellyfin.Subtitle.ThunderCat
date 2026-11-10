using Jellyfin.Plugin.ThunderCat.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThunderCat.Tests;

public sealed class ThunderProviderTimeoutTests
{
    [Fact]
    public async Task GetLocalCidAsync_ReturnsNullWhenCidBudgetExpires()
    {
        var path = await WriteTempFileAsync();
        var logger = new TestLogger();
        var provider = new ThunderProvider(
            logger,
            async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return "unreachable";
            });

        try
        {
            var result = await provider.GetLocalCidAsync(
                path,
                TimeSpan.FromMilliseconds(30),
                CancellationToken.None);

            Assert.Null(result);
            Assert.Contains(logger.Messages, message => message.Contains("timed out", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GetLocalCidAsync_PropagatesCallerCancellation()
    {
        var path = await WriteTempFileAsync();
        var provider = new ThunderProvider(
            new TestLogger(),
            async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return "unreachable";
            });
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                provider.GetLocalCidAsync(path, TimeSpan.FromSeconds(2), source.Token));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<string> WriteTempFileAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "thundercat-" + Guid.NewGuid().ToString("N") + ".bin");
        await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });
        return path;
    }

    private sealed class TestLogger : ILogger<ThunderProvider>
    {
        public List<string> Messages { get; } = new List<string>();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
