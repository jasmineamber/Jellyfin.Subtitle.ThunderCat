using System.Globalization;
using System.Security;
using System.Text.Json;
using Emby.Subtitle.ThunderCat.Infrastructure;
using Emby.Subtitle.ThunderCat.Models;
using Emby.Subtitle.ThunderCat.Utils;
using Xunit.Abstractions;

namespace Emby.Subtitle.ThunderCat.Tests;

public sealed class LiveSearchProbeTests
{
    private const string EnableVariable = "THUNDERCAT_LIVE_PROBE";
    private const string TitleVariable = "THUNDERCAT_PROBE_TITLE";
    private const string RequireMatchVariable = "THUNDERCAT_PROBE_REQUIRE_MATCH";
    private static readonly Uri SubtitleCatDomain = new Uri("https://subtitlecat.com/");
    private static readonly Uri ThunderEndpoint = new Uri("https://api-shoulei-ssl.xunlei.com/oracle/subtitle");
    private static readonly UrlPolicy SubtitleCatPolicy = new UrlPolicy("subtitlecat.com", "www.subtitlecat.com");
    private static readonly UrlPolicy ThunderPolicy = new UrlPolicy("api-shoulei-ssl.xunlei.com");
    private static readonly UrlPolicy ThunderDownloadPolicy = new UrlPolicy("subtitle.v.geilijiasu.com");
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ITestOutputHelper _output;

    public LiveSearchProbeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task CurrentUpstreams_DoNotAdmitConflictingIdentifiers_WhenProbeIsEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "1", StringComparison.Ordinal))
        {
            return;
        }

        var title = Environment.GetEnvironmentVariable(TitleVariable);
        Assert.False(string.IsNullOrWhiteSpace(title), $"Set {TitleVariable} before running the live probe.");

        var plan = SearchQueryBuilder.Create(title, null);
        Assert.NotNull(plan.MediaCode);

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        IReadOnlyList<ProbeCandidate>[] providerResults =
        {
            await ProbeSubtitleCatAsync(plan, cancellationSource.Token),
            await ProbeThunderAsync(plan, cancellationSource.Token)
        };
        var candidates = providerResults.SelectMany(result => result).ToArray();
        var accepted = candidates.Where(candidate => candidate.Match.IsEligible).ToArray();

        foreach (var providerResult in providerResults)
        {
            WriteProviderSummary(providerResult);
        }

        Assert.NotEmpty(candidates);
        Assert.DoesNotContain(accepted, candidate => candidate.Match.HasIdentifierConflict);
        if (string.Equals(Environment.GetEnvironmentVariable(RequireMatchVariable), "1", StringComparison.Ordinal))
        {
            Assert.Contains(accepted, candidate => candidate.Match.Kind >= SearchMatchKind.MediaCode);
        }
    }

    private static async Task<IReadOnlyList<ProbeCandidate>> ProbeSubtitleCatAsync(
        SearchQueryPlan plan,
        CancellationToken cancellationToken)
    {
        var candidates = new List<ProbeCandidate>();
        foreach (var query in plan.Queries)
        {
            var searchUri = new Uri(SubtitleCatDomain, "index.php?search=" + Uri.EscapeDataString(query));
            var html = await SafeHttpClient.GetStringAsync(
                searchUri,
                SubtitleCatPolicy,
                2 * 1024 * 1024,
                TimeSpan.FromSeconds(45),
                3,
                PluginMetadata.UserAgent,
                cancellationToken);

            var queryCandidates = SubtitleCatHtmlParser
                .ParseSearchResults(html, SubtitleCatDomain, 40)
                .Where(item => IsAllowedUri(item.DetailUri, SubtitleCatPolicy))
                .Select(item => new ProbeCandidate(
                    "SubtitleCat",
                    item.Title,
                    query,
                    SearchResultMatcher.Evaluate(plan.RankingTitle, item.Title, plan.MediaCode, false)))
                .ToArray();
            candidates.AddRange(queryCandidates);

            if (queryCandidates.Any(candidate => SearchResultMatcher.IsSufficientForQuery(
                    query,
                    plan.MediaCode,
                    candidate.Match)))
            {
                break;
            }
        }

        return candidates;
    }

    private static async Task<IReadOnlyList<ProbeCandidate>> ProbeThunderAsync(
        SearchQueryPlan plan,
        CancellationToken cancellationToken)
    {
        var candidates = new List<ProbeCandidate>();
        foreach (var query in plan.Queries)
        {
            var requestUri = new UriBuilder(ThunderEndpoint)
            {
                Query = "name=" + Uri.EscapeDataString(query)
            }.Uri;
            var bytes = await SafeHttpClient.GetBytesAsync(
                requestUri,
                ThunderPolicy,
                1024 * 1024,
                TimeSpan.FromSeconds(45),
                3,
                PluginMetadata.UserAgent,
                cancellationToken);
            var payload = JsonSerializer.Deserialize<ThunderSubtitleResponse>(bytes, JsonOptions);
            Assert.NotNull(payload);
            Assert.Equal(0, payload!.Code);

            var queryCandidates = (payload.Data ?? new List<ThunderSubtitleItem>())
                .Where(IsUsableThunderItem)
                .Take(100)
                .Select(item => new ProbeCandidate(
                    "Thunder",
                    item.Name!,
                    query,
                    SearchResultMatcher.Evaluate(plan.RankingTitle, item.Name, plan.MediaCode, false)))
                .ToArray();
            candidates.AddRange(queryCandidates);

            if (queryCandidates.Any(candidate => SearchResultMatcher.IsSufficientForQuery(
                    query,
                    plan.MediaCode,
                    candidate.Match)))
            {
                break;
            }
        }

        return candidates;
    }

    private static bool IsUsableThunderItem(ThunderSubtitleItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Name) ||
            string.IsNullOrWhiteSpace(item.Url) ||
            !Uri.TryCreate(item.Url, UriKind.Absolute, out var downloadUri) ||
            SubtitleFormat.Normalize(item.Ext, downloadUri) == null)
        {
            return false;
        }

        return IsAllowedUri(downloadUri, ThunderDownloadPolicy);
    }

    private static bool IsAllowedUri(Uri uri, UrlPolicy policy)
    {
        try
        {
            UrlSafetyValidator.ValidateSyntax(uri, policy);
            return true;
        }
        catch (SecurityException)
        {
            return false;
        }
    }

    private void WriteProviderSummary(IReadOnlyList<ProbeCandidate> candidates)
    {
        var provider = candidates.FirstOrDefault()?.Provider ?? "Unknown";
        var acceptedCount = candidates.Count(candidate => candidate.Match.IsEligible);
        var conflicts = candidates.Count(candidate => candidate.Match.HasIdentifierConflict);
        _output.WriteLine($"{provider}: total={candidates.Count}, accepted={acceptedCount}, conflicts={conflicts}");

        foreach (var candidate in candidates
                     .OrderByDescending(item => item.Match.IsEligible)
                     .ThenByDescending(item => item.Match.Rank)
                     .Take(15))
        {
            var status = candidate.Match.IsEligible ? "ACCEPT" : "REJECT";
            _output.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0,-6} {1,-9} score={2:0.000} query={3} name={4}",
                status,
                candidate.Match.Kind,
                candidate.Match.Similarity,
                candidate.Query,
                candidate.Name));
        }
    }

    private sealed class ProbeCandidate
    {
        public ProbeCandidate(string provider, string name, string query, SearchMatch match)
        {
            Provider = provider;
            Name = name;
            Query = query;
            Match = match;
        }

        public string Provider { get; }

        public string Name { get; }

        public string Query { get; }

        public SearchMatch Match { get; }
    }
}
