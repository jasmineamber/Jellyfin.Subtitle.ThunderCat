using System;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.ThunderCat.Utils
{
    internal static class MediaCodeExtractor
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(500);
        private static readonly Regex Fc2Pattern = new Regex(
            @"\bFC2[\s._-]*PPV[\s._-]*(?<number>\d{5,8})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout);
        private static readonly Regex SeparatedPattern = new Regex(
            @"\b(?<prefix>\d*[A-Z]{2,10})[-_]+(?<number>\d{2,7})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout);
        private static readonly Regex CompactPattern = new Regex(
            @"\b(?<prefix>[A-Z]{2,6})(?<number>\d{5,8})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout);

        public static string? Extract(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            var fc2 = Fc2Pattern.Match(title);
            if (fc2.Success)
            {
                return "FC2-PPV-" + fc2.Groups["number"].Value;
            }

            var separated = SeparatedPattern.Match(title);
            if (separated.Success && !IsGenericEpisodePrefix(separated.Groups["prefix"].Value))
            {
                return (separated.Groups["prefix"].Value + "-" + separated.Groups["number"].Value)
                    .ToUpperInvariant();
            }

            var compact = CompactPattern.Match(title);
            if (compact.Success && !IsGenericEpisodePrefix(compact.Groups["prefix"].Value))
            {
                return compact.Value.ToUpperInvariant();
            }

            return null;
        }

        internal static bool AreEquivalent(string? first, string? second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            {
                return false;
            }

            return string.Equals(
                RemoveSeparators(first!),
                RemoveSeparators(second!),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string RemoveSeparators(string value) =>
            value.Replace("-", string.Empty).Replace("_", string.Empty);

        private static bool IsGenericEpisodePrefix(string prefix) =>
            prefix.Equals("EP", StringComparison.OrdinalIgnoreCase) ||
            prefix.Equals("E", StringComparison.OrdinalIgnoreCase) ||
            prefix.Equals("S", StringComparison.OrdinalIgnoreCase);
    }
}
