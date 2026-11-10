using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ThunderCat.Infrastructure;
using Jellyfin.Plugin.ThunderCat.Models;
using Jellyfin.Plugin.ThunderCat.Utils;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThunderCat.Providers
{
    public sealed class ThunderProvider : ISubtitleProvider
    {
        private const string ProviderKey = "Thunder";
        private const int SearchResponseLimit = 1024 * 1024;
        private const int SubtitleResponseLimit = 8 * 1024 * 1024;
        private const int ResultLimit = 20;
        private const int MaximumCachedSearchBytes = 512 * 1024;
        private static readonly TimeSpan SearchAttemptTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan SearchBudget = TimeSpan.FromSeconds(12);
        private static readonly TimeSpan CidBudget = TimeSpan.FromSeconds(2);
        private static readonly Uri ApiEndpoint = new Uri("https://api-shoulei-ssl.xunlei.com/oracle/subtitle");
        private static readonly UrlPolicy ApiPolicy = new UrlPolicy("api-shoulei-ssl.xunlei.com");
        private static readonly UrlPolicy DownloadPolicy = new UrlPolicy("subtitle.v.geilijiasu.com");
        private static readonly SemaphoreSlim CidConcurrency = new SemaphoreSlim(2, 2);
        private static readonly ExpiringCache<byte[]> SearchCache = new ExpiringCache<byte[]>(
            TimeSpan.FromMinutes(5),
            16,
            StringComparer.OrdinalIgnoreCase);
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly ILogger<ThunderProvider> _logger;
        private readonly Func<string, CancellationToken, Task<string>> _calculateCidAsync;
        private readonly DownloadTokenStore _tokenStore = DownloadTokenStore.Shared;

        public ThunderProvider(ILogger<ThunderProvider> logger)
            : this(logger, ThunderCidCalculator.CalculateAsync)
        {
        }

        internal ThunderProvider(
            ILogger<ThunderProvider> logger,
            Func<string, CancellationToken, Task<string>> calculateCidAsync)
        {
            _logger = logger;
            _calculateCidAsync = calculateCidAsync;
        }

        public string Name => ProviderKey;

        public IEnumerable<VideoContentType> SupportedMediaTypes =>
            new[] { VideoContentType.Movie, VideoContentType.Episode };

        public async Task<IEnumerable<RemoteSubtitleInfo>> Search(
            SubtitleSearchRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!(ThunderCatPlugin.Instance?.Configuration.EnableThunder ?? true))
            {
                return Array.Empty<RemoteSubtitleInfo>();
            }

            var requestedLanguage = LanguageNormalizer.NormalizeForJellyfin(request.Language);
            var deduplicationKey = AutomaticDownloadGuard.CreateKey(request.MediaPath, requestedLanguage);
            if (request.IsAutomated && AutomaticDownloadGuard.ShouldSuppressSearch(deduplicationKey))
            {
                _logger.LogInformation(
                    "[Thunder] Suppressing duplicate automated search: item={ItemName}, language={Language}.",
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
                "[Thunder] Search start: item={ItemName}, mediaCode={MediaCode}, language={Language}, automated={Automated}, queries={Queries}.",
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
                        var localCid = await GetLocalCidAsync(
                                request.MediaPath,
                                CidBudget,
                                searchSource.Token)
                            .ConfigureAwait(false);

                        for (var index = 0; index < queryPlan.Queries.Count; index++)
                        {
                            searchSource.Token.ThrowIfCancellationRequested();
                            var queryCandidates = await SearchQueryAsync(
                                    queryPlan.Queries[index],
                                    queryPlan.RankingTitle,
                                    queryPlan.MediaCode,
                                    localCid,
                                    requestedLanguage,
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
                        _logger.LogWarning("[Thunder] Search budget exhausted; returning collected results.");
                    }
                    catch (TimeoutException)
                    {
                        _logger.LogWarning("[Thunder] Upstream search attempts timed out; returning collected results.");
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
                            candidate.Format,
                            request.IsAutomated ? deduplicationKey : null,
                            displayName: SearchResultMatcher.FormatDisplayName(
                                name,
                                candidate.Match.Kind),
                            mediaCode: queryPlan.MediaCode,
                            isAutomated: request.IsAutomated)),
                        Name = SearchResultMatcher.FormatDisplayName(name, candidate.Match.Kind),
                        ProviderName = Name,
                        ThreeLetterISOLanguageName = candidate.Language,
                        Format = candidate.Format,
                        IsHashMatch = candidate.Match.Kind == SearchMatchKind.Cid
                    });

                    if (ordered.Count >= ResultLimit)
                    {
                        break;
                    }
                }

                var topResults = string.Join(
                    " | ",
                    ordered
                        .Take(5)
                        .Select((result, index) =>
                            $"#{index + 1} {LogValue.Sanitize(result.Name, 120)} " +
                            $"[{result.ThreeLetterISOLanguageName}, format={result.Format}]"));
                _logger.LogInformation(
                    "[Thunder] Search completed: language={Language}, resultCount={ResultCount}, topResults={TopResults}.",
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
                _logger.LogError(ex, "[Thunder] Search failed.");
                return Array.Empty<RemoteSubtitleInfo>();
            }
        }

        public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_tokenStore.TryResolve(id, ProviderKey, out var request))
            {
                _logger.LogWarning("[Thunder] Rejected an invalid or expired download token.");
                throw new InvalidOperationException("The Thunder download token is invalid or has expired.");
            }

            try
            {
                _logger.LogInformation(
                    "[Thunder] Download attempt: item={ItemName}, mediaCode={MediaCode}, requestedLanguage={RequestedLanguage}, language={Language}, format={Format}, downloadHost={DownloadHost}.",
                    LogValue.Sanitize(request.DisplayName),
                    LogValue.Sanitize(request.MediaCode, 64),
                    LogValue.Sanitize(request.Language, 32),
                    request.Language,
                    request.Format,
                    request.Uri.Host);

                var bytes = await SafeHttpClient.GetBytesAsync(
                        request.Uri,
                        DownloadPolicy,
                        SubtitleResponseLimit,
                        TimeSpan.FromSeconds(20),
                        2,
                        PluginMetadata.UserAgent,
                        cancellationToken)
                    .ConfigureAwait(false);
                var inspection = SubtitleContentValidator.Inspect(bytes);
                if (!inspection.IsSupportedText)
                {
                    _logger.LogWarning(
                        "[Thunder] Candidate rejected: item={ItemName}, language={Language}, reason={Reason}, replacementCharacters={ReplacementCharacters}, mojibakeMarkers={MojibakeMarkers}, bytes={Bytes}.",
                        LogValue.Sanitize(request.DisplayName),
                        request.Language,
                        inspection.RejectionReason,
                        inspection.ReplacementCharacterCount,
                        inspection.MojibakeMarkerCount,
                        bytes.Length);
                    throw new InvalidDataException(
                        "Thunder returned unusable subtitle content: " +
                        inspection.RejectionReason);
                }

                AutomaticDownloadGuard.MarkSuccessfulDownload(request.DeduplicationKey);
                _logger.LogInformation(
                    "[Thunder] Selected subtitle: item={ItemName}, mediaCode={MediaCode}, language={Language}, format={Format}, bytes={Bytes}, replacementCharacters=0.",
                    LogValue.Sanitize(request.DisplayName),
                    LogValue.Sanitize(request.MediaCode, 64),
                    request.Language,
                    request.Format,
                    bytes.Length);

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
                _logger.LogError(ex, "[Thunder] Download failed.");
                throw;
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
                        SearchAttemptTimeout,
                        2,
                        PluginMetadata.UserAgent,
                        cancellationToken)
                    .ConfigureAwait(false);
                fetchedFromUpstream = true;
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new MemoryStream(responseBytes, false);
            var payload = JsonSerializer.Deserialize<ThunderSubtitleResponse>(stream, JsonOptions);

            if (payload == null || payload.Code != 0 || payload.Data == null)
            {
                _logger.LogWarning("[Thunder] Upstream response was unsuccessful or missing data.");
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

        internal async Task<string?> GetLocalCidAsync(
            string? mediaPath,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
            {
                return null;
            }

            var semaphoreEntered = false;
            using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutSource.CancelAfter(timeout);
                try
                {
                    await CidConcurrency.WaitAsync(timeoutSource.Token).ConfigureAwait(false);
                    semaphoreEntered = true;
                    return await _calculateCidAsync(mediaPath!, timeoutSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("[Thunder] CID calculation timed out; continuing without CID.");
                    return null;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Thunder] CID calculation failed.");
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
