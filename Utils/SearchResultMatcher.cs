using System;

namespace Emby.Subtitle.ThunderCat.Utils
{
    internal enum SearchMatchKind
    {
        Weak = 0,
        Title = 1,
        ExactTitle = 2,
        MediaCode = 3,
        Cid = 4
    }

    internal sealed class SearchMatch
    {
        public SearchMatch(
            SearchMatchKind kind,
            double similarity,
            long rank,
            bool isEligible,
            bool hasIdentifierConflict)
        {
            Kind = kind;
            Similarity = similarity;
            Rank = rank;
            IsEligible = isEligible;
            HasIdentifierConflict = hasIdentifierConflict;
        }

        public SearchMatchKind Kind { get; }

        public double Similarity { get; }

        public long Rank { get; }

        public bool IsEligible { get; }

        public bool HasIdentifierConflict { get; }
    }

    internal static class SearchResultMatcher
    {
        private const double TitleSimilarityThreshold = 0.55d;
        private const double CodeAwareTitleSimilarityThreshold = 0.65d;

        public static SearchMatch Evaluate(
            string? queryTitle,
            string? candidateTitle,
            string? queryCode,
            bool cidMatch)
        {
            var similarity = Similarity.GetTitleSimilarity(queryTitle ?? string.Empty, candidateTitle ?? string.Empty);
            var similarityRank = (long)Math.Round(similarity * 10000d);

            if (cidMatch)
            {
                return CreateEligible(SearchMatchKind.Cid, similarity, 1_000_000L + similarityRank);
            }

            var normalizedQueryCode = MediaCodeExtractor.Extract(queryCode) ?? queryCode?.Trim();
            var candidateCode = MediaCodeExtractor.Extract(candidateTitle);
            if (!string.IsNullOrWhiteSpace(normalizedQueryCode) &&
                !string.IsNullOrWhiteSpace(candidateCode))
            {
                if (MediaCodeExtractor.AreEquivalent(normalizedQueryCode, candidateCode))
                {
                    return CreateEligible(SearchMatchKind.MediaCode, similarity, 800_000L + similarityRank);
                }

                return new SearchMatch(
                    SearchMatchKind.Weak,
                    similarity,
                    -1L,
                    false,
                    true);
            }

            if (Similarity.AreEquivalentTitles(queryTitle, candidateTitle))
            {
                return CreateEligible(SearchMatchKind.ExactTitle, similarity, 600_000L + similarityRank);
            }

            var titleThreshold = string.IsNullOrWhiteSpace(normalizedQueryCode)
                ? TitleSimilarityThreshold
                : CodeAwareTitleSimilarityThreshold;
            if (similarity >= titleThreshold)
            {
                return CreateEligible(SearchMatchKind.Title, similarity, 300_000L + similarityRank);
            }

            return new SearchMatch(
                SearchMatchKind.Weak,
                similarity,
                similarityRank,
                false,
                false);
        }

        public static bool IsSufficientForQuery(string? query, string? queryCode, SearchMatch match)
        {
            if (!match.IsEligible)
            {
                return false;
            }

            var requiredMatch = !string.IsNullOrWhiteSpace(queryCode) &&
                                string.Equals(query, queryCode, StringComparison.OrdinalIgnoreCase)
                ? SearchMatchKind.MediaCode
                : SearchMatchKind.Title;
            return match.Kind >= requiredMatch;
        }

        public static string FormatDisplayName(string name, SearchMatchKind kind)
        {
            var prefix = kind switch
            {
                SearchMatchKind.Cid => "[CID] ",
                SearchMatchKind.MediaCode => "[ID] ",
                SearchMatchKind.ExactTitle => "[TITLE] ",
                _ => string.Empty
            };

            return prefix + name;
        }

        public static bool IsConfident(SearchMatchKind kind) => kind >= SearchMatchKind.Title;

        private static SearchMatch CreateEligible(SearchMatchKind kind, double similarity, long rank) =>
            new SearchMatch(kind, similarity, rank, true, false);
    }
}
