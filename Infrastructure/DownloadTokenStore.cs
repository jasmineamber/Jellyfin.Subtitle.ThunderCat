using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;

namespace Emby.Subtitle.ThunderCat.Infrastructure
{
    internal sealed class DownloadRequest
    {
        public DownloadRequest(string provider, Uri uri, string language, string format)
        {
            Provider = provider;
            Uri = uri;
            Language = language;
            Format = format;
        }

        public string Provider { get; }

        public Uri Uri { get; }

        public string Language { get; }

        public string Format { get; }
    }

    internal sealed class DownloadTokenStore
    {
        private const int MaxEntries = 4096;
        private static readonly TimeSpan EntryLifetime = TimeSpan.FromMinutes(20);
        private readonly ConcurrentDictionary<string, Entry> _entries =
            new ConcurrentDictionary<string, Entry>(StringComparer.Ordinal);

        public static DownloadTokenStore Shared { get; } = new DownloadTokenStore();

        public string Create(DownloadRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            PruneExpired();
            if (_entries.Count >= MaxEntries)
            {
                foreach (var stale in _entries.OrderBy(pair => pair.Value.ExpiresAt).Take(_entries.Count - MaxEntries + 1))
                {
                    _entries.TryRemove(stale.Key, out _);
                }
            }

            while (true)
            {
                var tokenBytes = new byte[32];
                using (var random = RandomNumberGenerator.Create())
                {
                    random.GetBytes(tokenBytes);
                }

                var token = "tc1_" + Convert.ToBase64String(tokenBytes)
                    .TrimEnd('=')
                    .Replace('+', '-')
                    .Replace('/', '_');

                if (_entries.TryAdd(token, new Entry(request, DateTimeOffset.UtcNow.Add(EntryLifetime))))
                {
                    return token;
                }
            }
        }

        public bool TryResolve(string token, string provider, out DownloadRequest request)
        {
            request = null!;
            if (string.IsNullOrWhiteSpace(token) || token.Length > 64 || !token.StartsWith("tc1_", StringComparison.Ordinal))
            {
                return false;
            }

            Entry entry;
            if (!_entries.TryGetValue(token, out entry!))
            {
                return false;
            }

            if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                _entries.TryRemove(token, out _);
                return false;
            }

            if (!string.Equals(entry.Request.Provider, provider, StringComparison.Ordinal))
            {
                return false;
            }

            request = entry.Request;
            return true;
        }

        private void PruneExpired()
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var pair in _entries)
            {
                if (pair.Value.ExpiresAt <= now)
                {
                    _entries.TryRemove(pair.Key, out _);
                }
            }
        }

        private sealed class Entry
        {
            public Entry(DownloadRequest request, DateTimeOffset expiresAt)
            {
                Request = request;
                ExpiresAt = expiresAt;
            }

            public DownloadRequest Request { get; }

            public DateTimeOffset ExpiresAt { get; }
        }
    }
}
