using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Emby.Subtitle.ThunderCat.Infrastructure;
using Emby.Subtitle.ThunderCat.Utils;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;

namespace Emby.Subtitle.ThunderCat.Providers
{
    public sealed class SubtitleCatProvider : ISubtitleProvider
    {
        private const string ProviderKey = "SubtitleCat";
                private const int SearchResponseLimit = 2 * 1024 * 1024;
        private const int DetailResponseLimit = 2 * 1024 * 1024;
        private const int SubtitleResponseLimit = 8 * 1024 * 1024;
        private static readonly Uri Domain = new Uri("https://subtitlecat.com/");
        private static readonly UrlPolicy SubtitleCatPolicy = new UrlPolicy("subtitlecat.com", "www.subtitlecat.com");

        private readonly ILogger _logger;
        private readonly DownloadTokenStore _tokenStore = DownloadTokenStore.Shared;

        public SubtitleCatProvider(ILogger logger)
        {
            _logger = logger;
        }

        public string Name => ProviderKey;

        public IEnumerable<VideoContentType> SupportedMediaTypes =>
            new[] { VideoContentType.Movie, VideoContentType.Episode };

        public async Task<IEnumerable<RemoteSubtitleInfo>> Search(
            SubtitleSearchRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!(Plugin.Instance?.Configuration.EnableSubtitleCat ?? true))
            {
                return Array.Empty<RemoteSubtitleInfo>();
            }

            var originalTitle = request.Name ?? string.Empty;
            var keyword = MediaCodeExtractor.Extract(originalTitle) ?? originalTitle;
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return Array.Empty<RemoteSubtitleInfo>();
            }

            var requestedLanguage = string.IsNullOrWhiteSpace(request.Language) ? "zh-CN" : request.Language;

            try
            {
                var searchUri = new Uri(Domain, "index.php?search=" + Uri.EscapeDataString(keyword));
                var html = await SafeHttpClient.GetStringAsync(
                        searchUri,
                        SubtitleCatPolicy,
                        SearchResponseLimit,
                        TimeSpan.FromSeconds(20),
                        2,
                        PluginMetadata.UserAgent,
                        cancellationToken)
                    .ConfigureAwait(false);

                var candidates = new List<Tuple<SubtitleCatSearchItem, double>>();
                foreach (var item in SubtitleCatHtmlParser.ParseSearchResults(html, Domain, 40))
                {
                    try
                    {
                        UrlSafetyValidator.ValidateSyntax(item.DetailUri, SubtitleCatPolicy);
                    }
                    catch (SecurityException)
                    {
                        continue;
                    }

                    candidates.Add(Tuple.Create(
                        item,
                        Similarity.GetTitleSimilarity(originalTitle, item.Title)));
                }

                var normalizedLanguage = LanguageNormalizer.NormalizeForEmby(requestedLanguage);
                var ordered = candidates
                    .OrderByDescending(candidate => candidate.Item2)
                    .Take(10)
                    .Select(candidate =>
                    {
                        var item = candidate.Item1;
                        return new RemoteSubtitleInfo
                        {
                            Id = _tokenStore.Create(new DownloadRequest(
                                ProviderKey,
                                item.DetailUri,
                                normalizedLanguage,
                                "srt")),
                            Name = item.Title,
                            ProviderName = Name,
                            Language = normalizedLanguage,
                            Format = "srt"
                        };
                    })
                    .ToArray();
                _logger.Info($"[SubtitleCat] Search completed with {ordered.Length} result(s).");
                return ordered;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException)
            {
                _logger.Warn("[SubtitleCat] Search timed out.");
                return Array.Empty<RemoteSubtitleInfo>();
            }
            catch (Exception ex)
            {
                _logger.Error($"[SubtitleCat] Search failed: {ex.GetType().Name}.");
                return Array.Empty<RemoteSubtitleInfo>();
            }
        }

        public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_tokenStore.TryResolve(id, ProviderKey, out var request))
            {
                _logger.Warn("[SubtitleCat] Rejected an invalid or expired download token.");
                return new SubtitleResponse();
            }

            try
            {
                var html = await SafeHttpClient.GetStringAsync(
                        request.Uri,
                        SubtitleCatPolicy,
                        DetailResponseLimit,
                        TimeSpan.FromSeconds(20),
                        2,
                        PluginMetadata.UserAgent,
                        cancellationToken)
                    .ConfigureAwait(false);

                var link = SubtitleCatHtmlParser.FindDownloadLink(html, request.Uri, request.Language);
                if (link == null)
                {
                    _logger.Warn("[SubtitleCat] No subtitle matched the requested language.");
                    return new SubtitleResponse();
                }

                UrlSafetyValidator.ValidateSyntax(link.Uri, SubtitleCatPolicy);
                var format = SubtitleFormat.Normalize(null, link.Uri);
                if (format == null)
                {
                    _logger.Warn("[SubtitleCat] Rejected an unsupported subtitle format.");
                    return new SubtitleResponse();
                }

                var bytes = await SafeHttpClient.GetBytesAsync(
                        link.Uri,
                        SubtitleCatPolicy,
                        SubtitleResponseLimit,
                        TimeSpan.FromSeconds(20),
                        2,
                        PluginMetadata.UserAgent,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!SubtitleContentValidator.IsSupportedText(bytes))
                {
                    _logger.Warn("[SubtitleCat] Rejected a non-text or upstream error response.");
                    return new SubtitleResponse();
                }

                return new SubtitleResponse
                {
                    Language = link.Language,
                    Format = format,
                    Stream = new MemoryStream(bytes, false)
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error($"[SubtitleCat] Download failed: {ex.GetType().Name}.");
                return new SubtitleResponse();
            }
        }
    }
}
