using System;
using System.Collections.Concurrent;
using Jellyfin.Plugin.ThunderCat.Utils;

namespace Jellyfin.Plugin.ThunderCat.Infrastructure
{
    internal static class AutomaticDownloadGuard
    {
        private const int MaxEntries = 512;
        private static readonly TimeSpan EntryLifetime = TimeSpan.FromMinutes(10);
        private static readonly ConcurrentDictionary<string, DateTimeOffset> CompletedDownloads =
            new ConcurrentDictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

        public static string? CreateKey(string? mediaPath, string? language)
        {
            if (string.IsNullOrWhiteSpace(mediaPath))
            {
                return null;
            }

            return mediaPath.Trim() + "\0" + LanguageNormalizer.NormalizeForJellyfin(language);
        }

        public static bool ShouldSuppressSearch(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (!CompletedDownloads.TryGetValue(key, out var expiresAt))
            {
                return false;
            }

            if (expiresAt > DateTimeOffset.UtcNow)
            {
                return true;
            }

            CompletedDownloads.TryRemove(key, out _);
            return false;
        }

        public static void MarkSuccessfulDownload(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var entry in CompletedDownloads)
            {
                if (entry.Value <= now)
                {
                    CompletedDownloads.TryRemove(entry.Key, out _);
                }
            }

            if (CompletedDownloads.Count >= MaxEntries)
            {
                foreach (var entry in CompletedDownloads)
                {
                    CompletedDownloads.TryRemove(entry.Key, out _);
                    if (CompletedDownloads.Count < MaxEntries)
                    {
                        break;
                    }
                }
            }

            CompletedDownloads[key] = now.Add(EntryLifetime);
        }

        internal static void ResetForTests()
        {
            CompletedDownloads.Clear();
        }
    }
}
