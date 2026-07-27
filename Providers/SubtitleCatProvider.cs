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
        private const int ResultLimit = 10;
        private const int MaximumCachedSearchCharacters = 256 * 1024;
        private static readonly Uri Domain = new Uri("https://subtitlecat.com/");
        private static readonly UrlPolicy SubtitleCatPolicy = new UrlPolicy("subtitlecat.com", "www.subtitlecat.com");
        private static readonly ExpiringCache<string> SearchCache = new ExpiringCache<string>(
            TimeSpan.FromMinutes(5),
            16,
            StringComparer.OrdinalIgnoreCase);

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

            var queryPlan = SearchQueryBuilder.Create(request.Name, request.MediaPath);
            if (queryPlan.Queries.Count == 0)
            {
                return Array.Empty<RemoteSubtitleInfo>();
            }

            var requestedLanguage = LanguageNormalizer.NormalizeForEmby(request.Language);

            try
            {
                var candidates = new List<RankedCandidate>();
                for (var index = 0; index < queryPlan.Queries.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var queryCandidates = await SearchQueryAsync(
                            queryPlan.Queries[index],
                            queryPlan.RankingTitle,
                            queryPlan.MediaCode,
                            cancellationToken)
                        .ConfigureAwait(false);
                    candidates.AddRange(queryCandidates);

                    if (queryCandidates.Any(candidate => SearchResultMatcher.IsSufficientForQuery(
                            queryPlan.Queries[index],
                            queryPlan.MediaCode,
                            candidate.Match)))
                    {
                        break;
                    }
                }

                var deduplicator = new SubtitleResultDeduplicator();
                var ordered = new List<RemoteSubtitleInfo>();
                foreach (var candidate in candidates
                             .Where(item => item.Match.IsEligible)
                             .OrderByDescending(item => item.Match.Rank)
                             .ThenBy(item => item.Item.Title, StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!deduplicator.TryAdd(candidate.Item.DetailUri))
                    {
                        continue;
                    }

                    ordered.Add(new RemoteSubtitleInfo
                    {
                        Id = _tokenStore.Create(new DownloadRequest(
                            ProviderKey,
                            candidate.Item.DetailUri,
                            requestedLanguage,
                            "srt")),
                        Name = SearchResultMatcher.FormatDisplayName(candidate.Item.Title, candidate.Match.Kind),
                        ProviderName = Name,
                        Language = requestedLanguage,
                        Format = "srt"
                    });

                    if (ordered.Count >= ResultLimit)
                    {
                        break;
                    }
                }

                _logger.Info($"[SubtitleCat] Search completed with {ordered.Count} result(s).");
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
                    Format = format,
                    Language = link.Language,
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

        private static async Task<IReadOnlyList<RankedCandidate>> SearchQueryAsync(
            string query,
            string originalTitle,
            string? extractedCode,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!SearchCache.TryGet(query, out var html))
            {
                var searchUri = new Uri(Domain, "index.php?search=" + Uri.EscapeDataString(query));
                html = await SafeHttpClient.GetStringAsync(
                        searchUri,
                        SubtitleCatPolicy,
                        SearchResponseLimit,
                        TimeSpan.FromSeconds(20),
                        2,
                        PluginMetadata.UserAgent,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (html.Length <= MaximumCachedSearchCharacters)
                {
                    SearchCache.Set(query, html);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var candidates = new List<RankedCandidate>();
            foreach (var item in SubtitleCatHtmlParser.ParseSearchResults(html, Domain, 40))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    UrlSafetyValidator.ValidateSyntax(item.DetailUri, SubtitleCatPolicy);
                }
                catch (SecurityException)
                {
                    continue;
                }

                candidates.Add(new RankedCandidate(
                    item,
                    SearchResultMatcher.Evaluate(originalTitle, item.Title, extractedCode, false)));
            }

            return candidates;
        }

        private sealed class RankedCandidate
        {
            public RankedCandidate(SubtitleCatSearchItem item, SearchMatch match)
            {
                Item = item;
                Match = match;
            }

            public SubtitleCatSearchItem Item { get; }

            public SearchMatch Match { get; }
        }
    }
}
