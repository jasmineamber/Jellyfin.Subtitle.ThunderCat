using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace Emby.Subtitle.ThunderCat.Utils
{
    internal sealed class SubtitleCatSearchItem
    {
        public SubtitleCatSearchItem(Uri detailUri, string title)
        {
            DetailUri = detailUri;
            Title = title;
        }

        public Uri DetailUri { get; }

        public string Title { get; }
    }

    internal sealed class SubtitleCatDownloadLink
    {
        public SubtitleCatDownloadLink(Uri uri, string language)
        {
            Uri = uri;
            Language = language;
        }

        public Uri Uri { get; }

        public string Language { get; }
    }

    internal static class SubtitleCatHtmlParser
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
        private static readonly Regex AnchorRegex = new Regex(
            @"<a\b(?<attrs>[^>]*)>(?<text>.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
            RegexTimeout);
        private static readonly Regex AttributeRegex = new Regex(
            @"(?<name>[A-Za-z_:][A-Za-z0-9_:.-]*)\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)')",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout);
        private static readonly Regex TagRegex = new Regex(
            @"<[^>]+>",
            RegexOptions.Singleline | RegexOptions.CultureInvariant,
            RegexTimeout);

        public static IReadOnlyList<SubtitleCatSearchItem> ParseSearchResults(string html, Uri baseUri, int maxResults)
        {
            if (string.IsNullOrEmpty(html) || maxResults <= 0)
            {
                return Array.Empty<SubtitleCatSearchItem>();
            }

            var results = new List<SubtitleCatSearchItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match anchor in AnchorRegex.Matches(html))
            {
                var attributes = ParseAttributes(anchor.Groups["attrs"].Value);
                string href;
                if (!attributes.TryGetValue("href", out href!) || href.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                Uri detailUri;
                if (!Uri.TryCreate(baseUri, WebUtility.HtmlDecode(href), out detailUri!) ||
                    detailUri.AbsolutePath.IndexOf("/subs/", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var title = WebUtility.HtmlDecode(TagRegex.Replace(anchor.Groups["text"].Value, string.Empty)).Trim();
                if (string.IsNullOrWhiteSpace(title) || !seen.Add(detailUri.AbsoluteUri))
                {
                    continue;
                }

                results.Add(new SubtitleCatSearchItem(detailUri, title));
                if (results.Count >= maxResults)
                {
                    break;
                }
            }

            return results;
        }

        public static SubtitleCatDownloadLink? FindDownloadLink(string html, Uri baseUri, string requestedLanguage)
        {
            if (string.IsNullOrEmpty(html))
            {
                return null;
            }

            var candidates = new List<Tuple<string, string>>();
            foreach (Match anchor in AnchorRegex.Matches(html))
            {
                var attributes = ParseAttributes(anchor.Groups["attrs"].Value);
                string id;
                string href;
                if (!attributes.TryGetValue("id", out id!) ||
                    !attributes.TryGetValue("href", out href!) ||
                    !id.StartsWith("download_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                candidates.Add(Tuple.Create(id.Substring("download_".Length), href));
            }

            var languageCandidates = LanguageNormalizer.GetSubtitleCatCandidates(requestedLanguage);
            var selected = candidates.FirstOrDefault(candidate =>
                languageCandidates.Any(language => language.Equals(candidate.Item1, StringComparison.OrdinalIgnoreCase)));

            if (selected == null)
            {
                return null;
            }

            Uri uri;
            if (!Uri.TryCreate(baseUri, WebUtility.HtmlDecode(selected.Item2), out uri!))
            {
                return null;
            }

            return new SubtitleCatDownloadLink(uri, LanguageNormalizer.NormalizeForEmby(selected.Item1));
        }

        private static Dictionary<string, string> ParseAttributes(string attributes)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match attribute in AttributeRegex.Matches(attributes))
            {
                var value = attribute.Groups["double"].Success
                    ? attribute.Groups["double"].Value
                    : attribute.Groups["single"].Value;
                result[attribute.Groups["name"].Value] = value;
            }

            return result;
        }
    }
}
