using System;
using System.Collections.Generic;
using System.IO;

namespace Emby.Subtitle.ThunderCat.Utils
{
    internal sealed class SearchQueryPlan
    {
        public SearchQueryPlan(string rankingTitle, string? mediaCode, IReadOnlyList<string> queries)
        {
            RankingTitle = rankingTitle;
            MediaCode = mediaCode;
            Queries = queries;
        }

        public string RankingTitle { get; }

        public string? MediaCode { get; }

        public IReadOnlyList<string> Queries { get; }
    }

    internal static class SearchQueryBuilder
    {
        private const int MaximumQueryLength = 240;

        public static SearchQueryPlan Create(string? name, string? mediaPath)
        {
            var metadataTitle = NormalizeQuery(name);
            var fileName = ResolveFileName(mediaPath);
            var rankingTitle = metadataTitle.Length > 0 ? metadataTitle : fileName;
            var mediaCode = MediaCodeExtractor.Extract(fileName) ?? MediaCodeExtractor.Extract(rankingTitle);
            var queries = new List<string>(3);

            AddDistinct(queries, mediaCode);
            AddDistinct(queries, rankingTitle);
            if (string.IsNullOrWhiteSpace(mediaCode))
            {
                AddDistinct(queries, fileName);
            }

            return new SearchQueryPlan(rankingTitle, mediaCode, queries);
        }

        internal static string ResolveFileName(string? mediaPath)
        {
            if (string.IsNullOrWhiteSpace(mediaPath) || ContainsControlCharacter(mediaPath!))
            {
                return string.Empty;
            }

            try
            {
                return NormalizeQuery(Path.GetFileNameWithoutExtension(mediaPath));
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
            catch (NotSupportedException)
            {
                return string.Empty;
            }
        }

        private static void AddDistinct(List<string> queries, string? value)
        {
            var query = NormalizeQuery(value);
            if (query.Length == 0)
            {
                return;
            }

            foreach (var existing in queries)
            {
                if (string.Equals(existing, query, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            queries.Add(query);
        }

        private static string NormalizeQuery(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return LimitLength(value!.Trim());
        }

        private static bool ContainsControlCharacter(string value)
        {
            foreach (var character in value)
            {
                if (char.IsControl(character))
                {
                    return true;
                }
            }

            return false;
        }

        private static string LimitLength(string value)
        {
            if (value.Length <= MaximumQueryLength)
            {
                return value;
            }

            var limited = value.Substring(0, MaximumQueryLength);
            if (limited.Length > 0 && char.IsHighSurrogate(limited[limited.Length - 1]))
            {
                limited = limited.Substring(0, limited.Length - 1);
            }

            return limited.TrimEnd();
        }
    }
}
