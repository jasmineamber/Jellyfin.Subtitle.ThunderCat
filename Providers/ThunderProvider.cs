using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Emby.Subtitle.ThunderCat.Infrastructure;
using Emby.Subtitle.ThunderCat.Models;
using Emby.Subtitle.ThunderCat.Utils;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;

namespace Emby.Subtitle.ThunderCat.Providers
{
    public sealed class ThunderProvider : ISubtitleProvider
    {
        private const string ProviderKey = "Thunder";
                private const int SearchResponseLimit = 1024 * 1024;
        private const int SubtitleResponseLimit = 8 * 1024 * 1024;
        private static readonly Uri ApiEndpoint = new Uri("https://api-shoulei-ssl.xunlei.com/oracle/subtitle");
        private static readonly UrlPolicy ApiPolicy = new UrlPolicy("api-shoulei-ssl.xunlei.com");
        private static readonly UrlPolicy DownloadPolicy = new UrlPolicy("subtitle.v.geilijiasu.com");
        private static readonly SemaphoreSlim CidConcurrency = new SemaphoreSlim(2, 2);

        private readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly DownloadTokenStore _tokenStore = DownloadTokenStore.Shared;

        public ThunderProvider(ILogger logger, IJsonSerializer jsonSerializer)
        {
            _logger = logger;
            _jsonSerializer = jsonSerializer;
        }

        public string Name => ProviderKey;

        public IEnumerable<VideoContentType> SupportedMediaTypes =>
            new[] { VideoContentType.Movie, VideoContentType.Episode };

        public async Task<IEnumerable<RemoteSubtitleInfo>> Search(
            SubtitleSearchRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!(Plugin.Instance?.Configuration.EnableThunder ?? true))
            {
                return Array.Empty<RemoteSubtitleInfo>();
            }

            var keyword = request.Name ?? string.Empty;
            var extractedCode = MediaCodeExtractor.Extract(keyword);
            var searchedByCode = !string.IsNullOrEmpty(extractedCode);
            if (searchedByCode)
            {
                keyword = extractedCode!;
            }

            if (string.IsNullOrWhiteSpace(keyword))
            {
                return Array.Empty<RemoteSubtitleInfo>();
            }

            string? localCid = null;
            if (!string.IsNullOrWhiteSpace(request.MediaPath) && File.Exists(request.MediaPath))
            {
                await CidConcurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    localCid = await ThunderCidCalculator.CalculateAsync(request.MediaPath, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Warn($"[Thunder] CID calculation failed: {ex.GetType().Name}.");
                }
                finally
                {
                    CidConcurrency.Release();
                }
            }

            try
            {
                var builder = new UriBuilder(ApiEndpoint)
                {
                    Query = "name=" + Uri.EscapeDataString(keyword)
                };

                var responseBytes = await SafeHttpClient.GetBytesAsync(
                        builder.Uri,
                        ApiPolicy,
                        SearchResponseLimit,
                        TimeSpan.FromSeconds(20),
                        2,
                        PluginMetadata.UserAgent,
                        cancellationToken)
                    .ConfigureAwait(false);

                ThunderSubtitleResponse payload;
                using (var stream = new MemoryStream(responseBytes, false))
                {
                    payload = _jsonSerializer.DeserializeFromStream<ThunderSubtitleResponse>(stream);
                }

                if (payload == null || payload.Code != 0 || payload.Data == null)
                {
                    _logger.Warn("[Thunder] Upstream response was unsuccessful or missing data.");
                    return Array.Empty<RemoteSubtitleInfo>();
                }

                var requestedLanguage = LanguageNormalizer.NormalizeForEmby(request.Language);
                var results = new List<Tuple<RemoteSubtitleInfo, long>>();

                foreach (var item in payload.Data.Take(100))
                {
                    if (string.IsNullOrWhiteSpace(item.Name) ||
                        string.IsNullOrWhiteSpace(item.Url) ||
                        !Uri.TryCreate(item.Url, UriKind.Absolute, out var downloadUri))
                    {
                        continue;
                    }

                    var format = SubtitleFormat.Normalize(item.Ext, downloadUri);
                    if (format == null)
                    {
                        continue;
                    }

                    try
                    {
                        UrlSafetyValidator.ValidateSyntax(downloadUri, DownloadPolicy);
                    }
                    catch (SecurityException)
                    {
                        continue;
                    }

                    var isHashMatch = !string.IsNullOrWhiteSpace(localCid) &&
                                      !string.IsNullOrWhiteSpace(item.Cid) &&
                                      string.Equals(localCid, item.Cid, StringComparison.OrdinalIgnoreCase);
                    var rank = (long)item.Score + (isHashMatch ? 1000L : 0L) + (searchedByCode ? 100L : 0L);
                    var resultLanguage = LanguageNormalizer.ResolveReportedLanguage(requestedLanguage, item.Languages);
                    var id = _tokenStore.Create(new DownloadRequest(
                        ProviderKey,
                        downloadUri,
                        resultLanguage,
                        format));

                    results.Add(Tuple.Create(
                        new RemoteSubtitleInfo
                        {
                            Id = id,
                            Name = isHashMatch ? "[⚡完美匹配] " + item.Name : item.Name,
                            ProviderName = Name,
                            Language = resultLanguage,
                            Format = format,
                            IsHashMatch = isHashMatch
                        },
                        rank));
                }

                var ordered = results
                    .OrderByDescending(result => result.Item1.IsHashMatch)
                    .ThenByDescending(result => result.Item2)
                    .Select(result => result.Item1)
                    .ToArray();
                _logger.Info($"[Thunder] Search completed with {ordered.Length} result(s).");
                return ordered;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException)
            {
                _logger.Warn("[Thunder] Search timed out.");
                return Array.Empty<RemoteSubtitleInfo>();
            }
            catch (Exception ex)
            {
                _logger.Error($"[Thunder] Search failed: {ex.GetType().Name}.");
                return Array.Empty<RemoteSubtitleInfo>();
            }
        }

        public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_tokenStore.TryResolve(id, ProviderKey, out var request))
            {
                _logger.Warn("[Thunder] Rejected an invalid or expired download token.");
                return new SubtitleResponse();
            }

            try
            {
                var bytes = await SafeHttpClient.GetBytesAsync(
                        request.Uri,
                        DownloadPolicy,
                        SubtitleResponseLimit,
                        TimeSpan.FromSeconds(20),
                        2,
                        PluginMetadata.UserAgent,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!SubtitleContentValidator.IsSupportedText(bytes))
                {
                    _logger.Warn("[Thunder] Rejected a non-text or upstream error response.");
                    return new SubtitleResponse();
                }

                return new SubtitleResponse
                {
                    Format = request.Format,
                    Language = request.Language,
                    Stream = new MemoryStream(bytes, false)
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error($"[Thunder] Download failed: {ex.GetType().Name}.");
                return new SubtitleResponse();
            }
        }
    }
}
