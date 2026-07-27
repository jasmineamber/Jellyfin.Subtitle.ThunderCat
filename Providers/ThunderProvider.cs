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
        private const int ResultLimit = 20;
        private const int MaximumCachedSearchBytes = 512 * 1024;
        private static readonly Uri ApiEndpoint = new Uri("https://api-shoulei-ssl.xunlei.com/oracle/subtitle");
        private static readonly UrlPolicy ApiPolicy = new UrlPolicy("api-shoulei-ssl.xunlei.com");
        private static readonly UrlPolicy DownloadPolicy = new UrlPolicy("subtitle.v.geilijiasu.com");
        private static readonly SemaphoreSlim CidConcurrency = new SemaphoreSlim(2, 2);
        private static readonly ExpiringCache<byte[]> SearchCache = new ExpiringCache<byte[]>(
            TimeSpan.FromMinutes(5),
            16,
            StringComparer.OrdinalIgnoreCase);

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

            var queryPlan = SearchQueryBuilder.Create(request.Name, request.MediaPath);
            if (queryPlan.Queries.Count == 0)
            {
                return Array.Empty<RemoteSubtitleInfo>();
            }

            var requestedLanguage = LanguageNormalizer.NormalizeForEmby(request.Language);
            var localCid = await GetLocalCidAsync(request.MediaPath, cancellationToken).ConfigureAwait(false);

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
                            localCid,
                            requestedLanguage,
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
                             .ThenByDescending(item => item.MatchesRequestedLanguage)
                             .ThenByDescending(item => item.UpstreamScore)
                             .ThenBy(item => item.Item.Name, StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = candidate.Item.Name!;
                    if (!deduplicator.TryAdd(candidate.DownloadUri))
                    {
                        continue;
                    }

                    ordered.Add(new RemoteSubtitleInfo
                    {
                        Id = _tokenStore.Create(new DownloadRequest(
                            ProviderKey,
                            candidate.DownloadUri,
                            candidate.Language,
                            candidate.Format)),
                        Name = SearchResultMatcher.FormatDisplayName(name, candidate.Match.Kind),
                        ProviderName = Name,
                        Language = candidate.Language,
                        Format = candidate.Format,
                        IsHashMatch = candidate.Match.Kind == SearchMatchKind.Cid
                    });

                    if (ordered.Count >= ResultLimit)
                    {
                        break;
                    }
                }

                _logger.Info($"[Thunder] Search completed with {ordered.Count} result(s).");
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

        private async Task<IReadOnlyList<RankedCandidate>> SearchQueryAsync(
            string query,
            string originalTitle,
            string? extractedCode,
            string? localCid,
            string requestedLanguage,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] responseBytes;
            var fetchedFromUpstream = false;
            if (!SearchCache.TryGet(query, out responseBytes!))
            {
                var builder = new UriBuilder(ApiEndpoint)
                {
                    Query = "name=" + Uri.EscapeDataString(query)
                };

                responseBytes = await SafeHttpClient.GetBytesAsync(
                        builder.Uri,
                        ApiPolicy,
                        SearchResponseLimit,
                        TimeSpan.FromSeconds(20),
                        2,
                        PluginMetadata.UserAgent,
                        cancellationToken)
                    .ConfigureAwait(false);
                fetchedFromUpstream = true;
            }

            cancellationToken.ThrowIfCancellationRequested();
            ThunderSubtitleResponse payload;
            using (var stream = new MemoryStream(responseBytes, false))
            {
                payload = _jsonSerializer.DeserializeFromStream<ThunderSubtitleResponse>(stream);
            }

            if (payload == null || payload.Code != 0 || payload.Data == null)
            {
                _logger.Warn("[Thunder] Upstream response was unsuccessful or missing data.");
                return Array.Empty<RankedCandidate>();
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (fetchedFromUpstream && responseBytes.Length <= MaximumCachedSearchBytes)
            {
                SearchCache.Set(query, responseBytes);
            }

            var results = new List<RankedCandidate>();
            foreach (var item in payload.Data.Take(100))
            {
                cancellationToken.ThrowIfCancellationRequested();
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

                var cidMatch = !string.IsNullOrWhiteSpace(localCid) &&
                               !string.IsNullOrWhiteSpace(item.Cid) &&
                               string.Equals(localCid, item.Cid, StringComparison.OrdinalIgnoreCase);
                var match = SearchResultMatcher.Evaluate(originalTitle, item.Name, extractedCode, cidMatch);
                var language = LanguageNormalizer.ResolveReportedLanguage(requestedLanguage, item.Languages);
                results.Add(new RankedCandidate(
                    item,
                    downloadUri,
                    format,
                    language,
                    match,
                    Math.Max(0, Math.Min(item.Score, 10000)),
                    LanguageNormalizer.ReportsRequestedLanguage(requestedLanguage, item.Languages)));
            }

            return results;
        }

        private async Task<string?> GetLocalCidAsync(string? mediaPath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
            {
                return null;
            }

            var semaphoreEntered = false;
            try
            {
                await CidConcurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
                semaphoreEntered = true;
                return await ThunderCidCalculator.CalculateAsync(mediaPath!, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warn($"[Thunder] CID calculation failed: {ex.GetType().Name}.");
                return null;
            }
            finally
            {
                if (semaphoreEntered)
                {
                    CidConcurrency.Release();
                }
            }
        }

        private sealed class RankedCandidate
        {
            public RankedCandidate(
                ThunderSubtitleItem item,
                Uri downloadUri,
                string format,
                string language,
                SearchMatch match,
                int upstreamScore,
                bool matchesRequestedLanguage)
            {
                Item = item;
                DownloadUri = downloadUri;
                Format = format;
                Language = language;
                Match = match;
                UpstreamScore = upstreamScore;
                MatchesRequestedLanguage = matchesRequestedLanguage;
            }

            public ThunderSubtitleItem Item { get; }

            public Uri DownloadUri { get; }

            public string Format { get; }

            public string Language { get; }

            public SearchMatch Match { get; }

            public int UpstreamScore { get; }

            public bool MatchesRequestedLanguage { get; }
        }
    }
}
