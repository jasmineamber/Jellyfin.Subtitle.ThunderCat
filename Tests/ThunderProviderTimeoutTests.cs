using System.Text;
using Emby.Subtitle.ThunderCat.Providers;
using MediaBrowser.Model.Logging;

namespace Emby.Subtitle.ThunderCat.Tests;

public sealed class ThunderProviderTimeoutTests
{
    [Fact]
    public async Task GetLocalCidAsync_ReturnsNullWhenCidBudgetExpires()
    {
        var path = await WriteTempFileAsync();
        var logger = new TestLogger();
        var provider = new ThunderProvider(
            logger,
            null!,
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
            null!,
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

    private sealed class TestLogger : ILogger
    {
        public List<string> Messages { get; } = new List<string>();

        public void Info(string message, params object[] paramList) => Messages.Add(message);

        public void Info(ReadOnlyMemory<char> message) => Messages.Add(message.ToString());

        public void Error(string message, params object[] paramList) => Messages.Add(message);

        public void Error(ReadOnlyMemory<char> message) => Messages.Add(message.ToString());

        public void Warn(string message, params object[] paramList) => Messages.Add(message);

        public void Warn(ReadOnlyMemory<char> message) => Messages.Add(message.ToString());

        public void Debug(string message, params object[] paramList) => Messages.Add(message);

        public void Debug(ReadOnlyMemory<char> message) => Messages.Add(message.ToString());

        public void Fatal(string message, params object[] paramList) => Messages.Add(message);

        public void FatalException(string message, Exception exception, params object[] paramList) => Messages.Add(message);

        public void ErrorException(string message, Exception exception, params object[] paramList) => Messages.Add(message);

        public void LogMultiline(string message, LogSeverity severity, StringBuilder additionalContent) => Messages.Add(message);

        public void Log(LogSeverity severity, string message, params object[] paramList) => Messages.Add(message);

        public void Log(LogSeverity severity, ReadOnlyMemory<char> message) => Messages.Add(message.ToString());
    }
}
