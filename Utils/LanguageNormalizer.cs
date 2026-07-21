using System;
using System.Collections.Generic;

namespace Emby.Subtitle.ThunderCat.Utils
{
    internal static class LanguageNormalizer
    {
        public static string NormalizeForEmby(string? language)
        {
            if (string.IsNullOrWhiteSpace(language))
            {
                return "chi";
            }

            var value = language!.Trim();
            switch (value.ToLowerInvariant())
            {
                case "zh":
                case "zh-cn":
                case "zh-tw":
                case "zh-hk":
                case "zh-hans":
                case "zh-hant":
                case "zho":
                case "chi":
                    return "chi";
                case "en":
                case "eng":
                    return "eng";
                case "es":
                case "spa":
                    return "spa";
                case "fr":
                case "fra":
                case "fre":
                    return "fre";
                case "de":
                case "deu":
                case "ger":
                    return "ger";
                case "it":
                case "ita":
                    return "ita";
                case "pt":
                case "por":
                    return "por";
                case "ja":
                case "jpn":
                    return "jpn";
                case "ko":
                case "kor":
                    return "kor";
                case "ru":
                case "rus":
                    return "rus";
                default:
                    return value;
            }
        }

        public static bool IsChinese(string? language) =>
            string.Equals(NormalizeForEmby(language), "chi", StringComparison.OrdinalIgnoreCase);

        public static string ResolveReportedLanguage(string? requestedLanguage, IEnumerable<string>? reportedLanguages)
        {
            if (reportedLanguages != null)
            {
                foreach (var language in reportedLanguages)
                {
                    if (!string.IsNullOrWhiteSpace(language))
                    {
                        return NormalizeForEmby(language);
                    }
                }
            }

            return NormalizeForEmby(requestedLanguage);
        }

        public static IReadOnlyList<string> GetSubtitleCatCandidates(string? language)
        {
            var requested = string.IsNullOrWhiteSpace(language) ? "zh-CN" : language!.Trim();
            switch (NormalizeForEmby(requested))
            {
                case "chi":
                    return new[] { requested, "zh-CN", "zh-Hans", "zh-TW", "zh-Hant", "zh-HK", "zho", "chi", "zh" };
                case "eng":
                    return new[] { requested, "en", "eng" };
                case "spa":
                    return new[] { requested, "es", "spa" };
                case "fre":
                    return new[] { requested, "fr", "fra", "fre" };
                case "ger":
                    return new[] { requested, "de", "deu", "ger" };
                case "ita":
                    return new[] { requested, "it", "ita" };
                case "por":
                    return new[] { requested, "pt", "por" };
                case "jpn":
                    return new[] { requested, "ja", "jpn" };
                case "kor":
                    return new[] { requested, "ko", "kor" };
                case "rus":
                    return new[] { requested, "ru", "rus" };
                default:
                    return new[] { requested };
            }
        }
    }
}
