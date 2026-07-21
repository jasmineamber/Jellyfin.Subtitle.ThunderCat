using System;
using System.Text.RegularExpressions;

namespace Emby.Subtitle.ThunderCat.Utils
{
    internal static class MediaCodeExtractor
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(500);
        private static readonly Regex Fc2Pattern = new Regex(
            @"\bFC2[\s._-]*PPV[\s._-]*(?<number>\d{5,8})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout);
        private static readonly Regex DashedPattern = new Regex(
            @"\b(?<prefix>\d*[A-Z]{2,10})-(?<number>\d{2,7})\b",
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

            var dashed = DashedPattern.Match(title);
            if (dashed.Success && !IsGenericEpisodePrefix(dashed.Groups["prefix"].Value))
            {
                return (dashed.Groups["prefix"].Value + "-" + dashed.Groups["number"].Value)
                    .ToUpperInvariant();
            }

            var compact = CompactPattern.Match(title);
            if (compact.Success && !IsGenericEpisodePrefix(compact.Groups["prefix"].Value))
            {
                return compact.Value.ToUpperInvariant();
            }

            return null;
        }

        private static bool IsGenericEpisodePrefix(string prefix) =>
            prefix.Equals("EP", StringComparison.OrdinalIgnoreCase) ||
            prefix.Equals("E", StringComparison.OrdinalIgnoreCase) ||
            prefix.Equals("S", StringComparison.OrdinalIgnoreCase);
    }
}
