using System;
using System.IO;

namespace Emby.Subtitle.ThunderCat.Utils
{
    internal static class SubtitleFormat
    {
        public static string? Normalize(string? format, Uri? uri = null)
        {
            var candidate = format;
            if (string.IsNullOrWhiteSpace(candidate) && uri != null)
            {
                candidate = Path.GetExtension(uri.AbsolutePath).TrimStart('.');
            }

            if (string.IsNullOrWhiteSpace(candidate))
            {
                return null;
            }

            var normalized = candidate!.Trim().TrimStart('.').ToLowerInvariant();
            switch (normalized)
            {
                case "srt":
                case "ass":
                case "ssa":
                case "vtt":
                    return normalized;
                default:
                    return null;
            }
        }
    }
}
