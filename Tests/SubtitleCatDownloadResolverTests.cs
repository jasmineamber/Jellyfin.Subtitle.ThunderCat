using System.Net.Http;
using System.Text;
using Jellyfin.Plugin.ThunderCat.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThunderCat.Tests;

public sealed class SubtitleCatDownloadResolverTests
{
    [Fact]
    public async Task ResolveAsync_PrefersSimplifiedFromLaterCandidateBeforeTraditional()
    {
        var firstDetail = new Uri("https://subtitlecat.com/subs/951/ABP-290.html");
        var secondDetail = new Uri("https://subtitlecat.com/subs/1487/ABP-290-jp.html");
        var requestedDownloads = new List<Uri>();
        var logger = new TestLogger();
        var resolver = new SubtitleCatDownloadResolver(
            logger,
            (uri, _) => Task.FromResult(uri == firstDetail
                ? """
                  <a id="download_zh-CN" href="/subs/1002/ABP-290-zh-CN.srt">CN</a>
                  <a id="download_zh-TW" href="/subs/953/ABP-290-zh-TW.srt">TW</a>
                  """
                : """
                  <a id="download_zh-CN" href="/subs/1766/ABP-290-jp-zh-CN.srt">CN</a>
                  """),
            (uri, _) =>
            {
                requestedDownloads.Add(uri);
                if (uri.AbsolutePath.Contains("/subs/1002/", StringComparison.Ordinal))
                {
                    throw new HttpRequestException("The upstream returned HTTP 404.");
                }

                return Task.FromResult(Encoding.UTF8.GetBytes(
                    "1\n00:00:01,000 --> 00:00:02,000\n欢迎\n"));
            });

        var result = await resolver.ResolveAsync(
            new[] { firstDetail, secondDetail },
            "chi",
            "[ID] ABP-290",
            "ABP-290",
            CancellationToken.None);

        Assert.Equal(secondDetail, result.DetailUri);
        Assert.Equal("zh-CN", result.Variant);
        Assert.Equal("chi", result.Language);
        Assert.DoesNotContain(
            requestedDownloads,
            uri => uri.AbsolutePath.Contains("zh-TW", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logger.Messages, message => message.Contains("requestedLanguage=chi", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("Candidate download failed", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("variant=zh-CN", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolveAsync_RejectsReplacementCharactersAndUsesNextCleanCandidate()
    {
        var firstDetail = new Uri("https://subtitlecat.com/subs/272/ABP-347.html");
        var secondDetail = new Uri("https://subtitlecat.com/subs/451/ABP-347-clean.html");
        var logger = new TestLogger();
        var resolver = new SubtitleCatDownloadResolver(
            logger,
            (uri, _) => Task.FromResult(
                $"<a id=\"download_zh-CN\" href=\"/subs/{(uri == firstDetail ? "307" : "452")}/ABP-347-zh-CN.srt\">CN</a>"),
            (uri, _) => Task.FromResult(uri.AbsolutePath.Contains("/subs/307/", StringComparison.Ordinal)
                ? Encoding.UTF8.GetBytes(
                    "00:00:01,000 --> 00:00:02,000\r\n\uFFFD\uFFFD\uFFFD\r\n")
                : Encoding.UTF8.GetBytes(
                    "00:00:01,000 --> 00:00:02,000\r\n这是正常简体字幕\r\n")));

        var result = await resolver.ResolveAsync(
            new[] { firstDetail, secondDetail },
            "chi",
            "[ID] ABP-347 上原瑞穂",
            "ABP-347",
            CancellationToken.None);

        Assert.Equal(secondDetail, result.DetailUri);
        Assert.Equal("zh-CN", result.Variant);
        Assert.StartsWith("1\r\n", Encoding.UTF8.GetString(result.Bytes), StringComparison.Ordinal);
        Assert.DoesNotContain('\uFFFD', Encoding.UTF8.GetString(result.Bytes));
        Assert.Contains(logger.Messages, message => message.Contains("reason=replacement-characters", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("replacementCharacters=3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolveAsync_FallsBackToTraditionalWhenSimplifiedIsUnavailable()
    {
        var detail = new Uri("https://subtitlecat.com/subs/100/Test.html");
        var resolver = new SubtitleCatDownloadResolver(
            new TestLogger(),
            (_, _) => Task.FromResult(
                """
                <a id="download_zh-TW" href="/subs/102/Test-zh-TW.srt">TW</a>
                """),
            (_, _) => Task.FromResult(Encoding.UTF8.GetBytes(
                "1\n00:00:01,000 --> 00:00:02,000\n歡迎\n")));

        var result = await resolver.ResolveAsync(
            new[] { detail },
            "chi",
            "[ID] Test",
            "TEST-001",
            CancellationToken.None);

        Assert.Equal("zh-TW", result.Variant);
        Assert.Equal("chi", result.Language);
    }

    private sealed class TestLogger : ILogger<SubtitleCatProvider>
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
