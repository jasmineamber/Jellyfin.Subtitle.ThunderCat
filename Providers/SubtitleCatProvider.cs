using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ThunderCat.Infrastructure;
using Jellyfin.Plugin.ThunderCat.Utils;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThunderCat.Providers
{
    public sealed class SubtitleCatProvider : ISubtitleProvider
    {
        private const string ProviderKey = "SubtitleCat";
        private const int SearchResponseLimit = 2 * 1024 * 1024;
        private const int DetailResponseLimit = 2 * 1024 * 1024;
        private const int SubtitleResponseLimit = 8 * 1024 * 1024;
        private const int ResultLimit = 10;
        private const int MaximumCachedSearchCharacters = 256 * 1024;
        private static readonly TimeSpan SearchAttemptTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan SearchBudget = TimeSpan.FromSeconds(12);
        private static readonly Uri Domain = new Uri("https://subtitlecat.com/");
        private static readonly UrlPolicy SubtitleCatPolicy = new UrlPolicy("subtitlecat.com", "www.subtitlecat.com");
        private static readonly ExpiringCache<string> SearchCache = new ExpiringCache<string>(
            TimeSpan.FromMinutes(5),
            16,
            StringComparer.OrdinalIgnoreCase);

        private readonly ILogger<SubtitleCatProvider> _logger;
        private readonly DownloadTokenStore _tokenStore = DownloadTokenStore.Shared;

        public SubtitleCatProvider(ILogger<SubtitleCatProvider> logger)
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

            if (!(ThunderCatPlugin.Instance?.Configuration.EnableSubtitleCat ?? true))
            {
                return Array.Empty<RemoteSubtitleInfo>();
            }

            var requestedLanguage = LanguageNormalizer.NormalizeForJellyfin(request.Language);
            var deduplicationKey = AutomaticDownloadGuard.CreateKey(request.MediaPath, requestedLanguage);
            if (request.IsAutomated && AutomaticDownloadGuard.ShouldSuppressSearch(deduplicationKey))
            {
                _logger.LogInformation(
                    "[SubtitleCat] Suppressing duplicate automated search: item={ItemName}, language={Language}.",
                    LogValue.Sanitize(request.Name),
                    requestedLanguage);
                return Array.Empty<RemoteSubtitleInfo>();
            }

            var queryPlan = SearchQueryBuilder.Create(request.Name, request.MediaPath);
            if (queryPlan.Queries.Count == 0)
            {
                return Array.Empty<RemoteSubtitleInfo>();
            }

            var queries = string.Join(
                " | ",
                queryPlan.Queries.Select(query => LogValue.Sanitize(query, 240)));
            _logger.LogInformation(
                "[SubtitleCat] Search start: item={ItemName}, mediaCode={MediaCode}, language={Language}, automated={Automated}, queries={Queries}.",
                LogValue.Sanitize(request.Name),
                LogValue.Sanitize(queryPlan.MediaCode, 64),
                requestedLanguage,
                request.IsAutomated,
                queries);

            try
            {
                var candidates = new List<RankedCandidate>();
                using (var searchSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    searchSource.CancelAfter(SearchBudget);
                    try
                    {
                        for (var index = 0; index < queryPlan.Queries.Count; index++)
                        {
                            searchSource.Token.ThrowIfCancellationRequested();
                            var queryCandidates = await SearchQueryAsync(
                                    queryPlan.Queries[index],
                                    queryPlan.RankingTitle,
                                    queryPlan.MediaCode,
                                    searchSource.Token)
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
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (OperationCanceledException) when (searchSource.IsCancellationRequested)
                    {
                        _logger.LogWarning("[SubtitleCat] Search budget exhausted; returning collected results.");
                    }
                    catch (TimeoutException)
                    {
                        _logger.LogWarning("[SubtitleCat] Upstream search attempts timed out; returning collected results.");
                    }
                }

                var deduplicator = new SubtitleResultDeduplicator();
                var selectedCandidates = new List<RankedCandidate>();
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

                    selectedCandidates.Add(candidate);
                    if (selectedCandidates.Count >= ResultLimit)
                    {
                        break;
                    }
                }

                var automaticCandidateUris = request.IsAutomated
                    ? selectedCandidates.Select(candidate => candidate.Item.DetailUri).ToArray()
                    : null;
                var ordered = new List<RemoteSubtitleInfo>();
                for (var index = 0; index < selectedCandidates.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var candidate = selectedCandidates[index];
                    var displayName = SearchResultMatcher.FormatDisplayName(
                        candidate.Item.Title,
                        candidate.Match.Kind);
                    var tokenCandidates = request.IsAutomated
                        ? automaticCandidateUris
                        : new[] { candidate.Item.DetailUri };

                    ordered.Add(new RemoteSubtitleInfo
                    {
                        Id = _tokenStore.Create(new DownloadRequest(
                            ProviderKey,
                            candidate.Item.DetailUri,
                            requestedLanguage,
                            "srt",
                            request.IsAutomated ? deduplicationKey : null,
                            tokenCandidates,
                            displayName,
                            queryPlan.MediaCode,
                            request.IsAutomated)),
                        Name = displayName,
                        ProviderName = Name,
                        ThreeLetterISOLanguageName = requestedLanguage,
                        Format = "srt"
                    });
                }

                var topResults = string.Join(
                    " | ",
                    selectedCandidates
                        .Take(5)
                        .Select((candidate, index) =>
                            $"#{index + 1} {LogValue.Sanitize(candidate.Item.Title, 120)} " +
                            $"[{requestedLanguage}, rank={candidate.Match.Rank}]"));
                _logger.LogInformation(
                    "[SubtitleCat] Search completed: language={Language}, resultCount={ResultCount}, topResults={TopResults}.",
                    requestedLanguage,
                    ordered.Count,
                    topResults);
                return ordered;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SubtitleCat] Search failed.");
                return Array.Empty<RemoteSubtitleInfo>();
            }
        }

        public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_tokenStore.TryResolve(id, ProviderKey, out var request))
            {
                _logger.LogWarning("[SubtitleCat] Rejected an invalid or expired download token.");
                throw new InvalidOperationException("The SubtitleCat download token is invalid or has expired.");
            }

            try
            {
                var resolver = new SubtitleCatDownloadResolver(
                    _logger,
                    GetDetailAsync,
                    GetDownloadAsync);
                var resolved = await resolver.ResolveAsync(
                        request.CandidateUris,
                        request.Language,
                        request.DisplayName,
                        request.MediaCode,
                        cancellationToken)
                    .ConfigureAwait(false);
                AutomaticDownloadGuard.MarkSuccessfulDownload(request.DeduplicationKey);

                return new SubtitleResponse
                {
                    Format = resolved.Format,
                    Language = resolved.Language,
                    Stream = new MemoryStream(resolved.Bytes, false)
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SubtitleCat] Download failed.");
                throw;
            }
        }

        private static Task<string> GetDetailAsync(Uri uri, CancellationToken cancellationToken) =>
            SafeHttpClient.GetStringAsync(
                uri,
                SubtitleCatPolicy,
                DetailResponseLimit,
                TimeSpan.FromSeconds(20),
                2,
                PluginMetadata.UserAgent,
                cancellationToken);

        private static Task<byte[]> GetDownloadAsync(Uri uri, CancellationToken cancellationToken) =>
            SafeHttpClient.GetBytesAsync(
                uri,
                SubtitleCatPolicy,
                SubtitleResponseLimit,
                TimeSpan.FromSeconds(20),
                2,
                PluginMetadata.UserAgent,
                cancellationToken);

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
                        SearchAttemptTimeout,
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
