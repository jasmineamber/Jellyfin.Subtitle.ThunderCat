using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.Subtitle.ThunderCat.Infrastructure
{
    internal sealed class ProviderProbeDefinition
    {
        public ProviderProbeDefinition(string provider, Uri uri, UrlPolicy policy)
        {
            Provider = provider;
            Uri = uri;
            Policy = policy;
        }

        public string Provider { get; }

        public Uri Uri { get; }

        public UrlPolicy Policy { get; }
    }

    internal sealed class ProviderHealthResult
    {
        public ProviderHealthResult(
            string provider,
            bool isAvailable,
            string status,
            long elapsedMilliseconds)
        {
            Provider = provider;
            IsAvailable = isAvailable;
            Status = status;
            ElapsedMilliseconds = elapsedMilliseconds;
        }

        public string Provider { get; }

        public bool IsAvailable { get; }

        public string Status { get; }

        public long ElapsedMilliseconds { get; }
    }

    internal static class ProviderHealthChecker
    {
        private const int ResponseLimit = 512 * 1024;
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(8);
        private static readonly ProviderProbeDefinition SubtitleCatProbe = new ProviderProbeDefinition(
            "SubtitleCat",
            new Uri("https://subtitlecat.com/"),
            new UrlPolicy("subtitlecat.com", "www.subtitlecat.com"));
        private static readonly ProviderProbeDefinition ThunderProbe = new ProviderProbeDefinition(
            "Thunder",
            new Uri("https://api-shoulei-ssl.xunlei.com/oracle/subtitle?name=thundercat-connectivity-test"),
            new UrlPolicy("api-shoulei-ssl.xunlei.com"));

        public static bool TryGetProbeDefinition(string? provider, out ProviderProbeDefinition definition)
        {
            if (string.Equals(provider, SubtitleCatProbe.Provider, StringComparison.OrdinalIgnoreCase))
            {
                definition = SubtitleCatProbe;
                return true;
            }

            if (string.Equals(provider, ThunderProbe.Provider, StringComparison.OrdinalIgnoreCase))
            {
                definition = ThunderProbe;
                return true;
            }

            definition = null!;
            return false;
        }

        public static async Task<ProviderHealthResult> CheckAsync(
            string? provider,
            CancellationToken cancellationToken)
        {
            if (!TryGetProbeDefinition(provider, out var definition))
            {
                return new ProviderHealthResult(
                    provider ?? string.Empty,
                    false,
                    "unsupported",
                    0);
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var bytes = await SafeHttpClient.GetBytesAsync(
                        definition.Uri,
                        definition.Policy,
                        ResponseLimit,
                        ProbeTimeout,
                        1,
                        PluginMetadata.UserAgent,
                        cancellationToken)
                    .ConfigureAwait(false);

                return new ProviderHealthResult(
                    definition.Provider,
                    bytes.Length > 0,
                    bytes.Length > 0 ? "ok" : "empty",
                    stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException)
            {
                return new ProviderHealthResult(
                    definition.Provider,
                    false,
                    "timeout",
                    stopwatch.ElapsedMilliseconds);
            }
            catch
            {
                return new ProviderHealthResult(
                    definition.Provider,
                    false,
                    "unavailable",
                    stopwatch.ElapsedMilliseconds);
            }
        }
    }
}
