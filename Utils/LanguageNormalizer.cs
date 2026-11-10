using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.ThunderCat.Utils
{
    internal static class LanguageNormalizer
    {
        public static string NormalizeForJellyfin(string? language)
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
                case "chs":
                case "cht":
                case "简体":
                case "繁体":
                case "中文":
                    return "chi";
                case "en":
                case "eng":
                case "英语":
                case "英文":
                case "english":
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
                case "日语":
                case "日文":
                case "japanese":
                    return "jpn";
                case "ko":
                case "kor":
                case "韩语":
                case "韩文":
                case "korean":
                    return "kor";
                case "ru":
                case "rus":
                    return "rus";
                default:
                    return value;
            }
        }

        public static bool IsChinese(string? language) =>
            string.Equals(NormalizeForJellyfin(language), "chi", StringComparison.OrdinalIgnoreCase);

        public static bool ReportsRequestedLanguage(
            string? requestedLanguage,
            IEnumerable<string>? reportedLanguages)
        {
            var requested = NormalizeForJellyfin(requestedLanguage);
            if (reportedLanguages == null)
            {
                return false;
            }

            foreach (var language in reportedLanguages)
            {
                foreach (var normalized in GetReportedLanguageCandidates(language))
                {
                    if (string.Equals(normalized, requested, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static string ResolveReportedLanguage(string? requestedLanguage, IEnumerable<string>? reportedLanguages)
        {
            var requested = NormalizeForJellyfin(requestedLanguage);
            string? firstRecognized = null;
            if (reportedLanguages != null)
            {
                foreach (var language in reportedLanguages)
                {
                    foreach (var normalized in GetReportedLanguageCandidates(language))
                    {
                        if (string.Equals(normalized, requested, StringComparison.OrdinalIgnoreCase))
                        {
                            return requested;
                        }

                        if (firstRecognized == null)
                        {
                            firstRecognized = normalized;
                        }
                    }
                }
            }

            return firstRecognized ?? requested;
        }

        private static IReadOnlyList<string> GetReportedLanguageCandidates(string? language)
        {
            if (string.IsNullOrWhiteSpace(language))
            {
                return Array.Empty<string>();
            }

            var value = language!.Trim();
            if (value.Equals("默认", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("未知语言", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("SubtitleHandler", StringComparison.OrdinalIgnoreCase))
            {
                return Array.Empty<string>();
            }

            var candidates = new List<string>(2);
            if (value.IndexOf("简体", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("繁体", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("中文", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("Chinese", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                candidates.Add("chi");
            }

            if (value.IndexOf("英语", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("英文", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("English", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                candidates.Add("eng");
            }

            if (value.IndexOf("日语", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("日文", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("Japanese", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                candidates.Add("jpn");
            }

            if (value.IndexOf("韩语", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("韩文", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("Korean", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                candidates.Add("kor");
            }

            if (candidates.Count == 0)
            {
                var normalized = NormalizeForJellyfin(value);
                if (IsKnownLanguage(normalized))
                {
                    candidates.Add(normalized);
                }
            }

            return candidates;
        }

        private static bool IsKnownLanguage(string language)
        {
            switch (language.ToLowerInvariant())
            {
                case "chi":
                case "eng":
                case "spa":
                case "fre":
                case "ger":
                case "ita":
                case "por":
                case "jpn":
                case "kor":
                case "rus":
                    return true;
                default:
                    return false;
            }
        }

        public static IReadOnlyList<string> GetSubtitleCatCandidates(string? language)
        {
            return GetSubtitleCatLanguagePreferences(language);
        }

        public static IReadOnlyList<string> GetSubtitleCatLanguagePreferences(string? language)
        {
            var requested = string.IsNullOrWhiteSpace(language) ? "zh-CN" : language!.Trim();
            switch (NormalizeForJellyfin(requested))
            {
                case "chi":
                    var requestedLower = requested.ToLowerInvariant();
                    if (requestedLower is "zh-tw" or "zh-hk" or "zh-hant" or "cht")
                    {
                        return Distinct(new[]
                        {
                            requested,
                            "zh-TW",
                            "zh-Hant",
                            "zh-HK",
                            "zh-CN",
                            "zh-Hans",
                            "zh",
                            "chi",
                            "zho"
                        });
                    }

                    if (requestedLower is "zh-cn" or "zh-hans" or "chs")
                    {
                        return Distinct(new[]
                        {
                            requested,
                            "zh-CN",
                            "zh-Hans",
                            "zh",
                            "chi",
                            "zho",
                            "zh-TW",
                            "zh-Hant",
                            "zh-HK"
                        });
                    }

                    return new[]
                    {
                        "zh-CN",
                        "zh-Hans",
                        "zh",
                        "chi",
                        "zho",
                        "zh-TW",
                        "zh-Hant",
                        "zh-HK"
                    };
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

        private static List<string> Distinct(IEnumerable<string> values)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
                {
                    result.Add(value);
                }
            }

            return result;
        }
    }
}
