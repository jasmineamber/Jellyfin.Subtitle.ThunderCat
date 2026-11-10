using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Jellyfin.Plugin.ThunderCat.Utils
{
    public static class Similarity
    {
        private static readonly char[] TokenSeparators = { ' ' };

        /// <summary>
        /// Calculates a filename-friendly title similarity score using normalized tokens and ordered n-grams.
        /// </summary>
        public static double GetTitleSimilarity(string source, string target)
        {
            var normalizedSource = Normalize(source);
            var normalizedTarget = Normalize(target);
            if (normalizedSource.Length == 0 || normalizedTarget.Length == 0)
            {
                return 0d;
            }

            if (string.Equals(normalizedSource, normalizedTarget, StringComparison.Ordinal))
            {
                return 1d;
            }

            var tokenScore = CalculateJaccard(
                SplitTokens(normalizedSource),
                SplitTokens(normalizedTarget));
            var gramScore = CalculateDice(
                BuildNGrams(normalizedSource),
                BuildNGrams(normalizedTarget));

            return Math.Max(0d, Math.Min(1d, (gramScore * 0.7d) + (tokenScore * 0.3d)));
        }

        public static double GetJaccardSimilarity(string source, string target) =>
            GetTitleSimilarity(source, target);

        internal static bool AreEquivalentTitles(string? source, string? target)
        {
            var normalizedSource = NormalizeForComparison(source);
            return normalizedSource.Length > 0 &&
                   string.Equals(normalizedSource, NormalizeForComparison(target), StringComparison.Ordinal);
        }

        internal static string NormalizeForComparison(string? value) => Normalize(value ?? string.Empty);

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            var previousWasSeparator = true;
            foreach (var character in value.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(character);
                    previousWasSeparator = false;
                }
                else if (!previousWasSeparator)
                {
                    builder.Append(' ');
                    previousWasSeparator = true;
                }
            }

            return builder.ToString().Trim();
        }

        private static HashSet<string> SplitTokens(string value) =>
            new HashSet<string>(
                value.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);

        private static HashSet<string> BuildNGrams(string value)
        {
            var compact = value.Replace(" ", string.Empty);
            if (compact.Length == 0)
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var size = compact.Length < 3 ? 1 : 3;
            var grams = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index <= compact.Length - size; index++)
            {
                grams.Add(compact.Substring(index, size));
            }

            return grams;
        }

        private static double CalculateJaccard(HashSet<string> source, HashSet<string> target)
        {
            if (source.Count == 0 || target.Count == 0)
            {
                return 0d;
            }

            var intersection = source.Intersect(target).Count();
            var union = source.Count + target.Count - intersection;
            return union == 0 ? 0d : (double)intersection / union;
        }

        private static double CalculateDice(HashSet<string> source, HashSet<string> target)
        {
            if (source.Count == 0 || target.Count == 0)
            {
                return 0d;
            }

            var intersection = source.Intersect(target).Count();
            return (2d * intersection) / (source.Count + target.Count);
        }
    }
}
