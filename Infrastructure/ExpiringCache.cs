using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.ThunderCat.Infrastructure
{
    internal sealed class ExpiringCache<T>
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, CacheEntry> _entries;
        private readonly TimeSpan _lifetime;
        private readonly int _capacity;
        private readonly Func<DateTimeOffset> _clock;

        public ExpiringCache(
            TimeSpan lifetime,
            int capacity,
            IEqualityComparer<string>? comparer = null,
            Func<DateTimeOffset>? clock = null)
        {
            if (lifetime <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(lifetime));
            }

            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _lifetime = lifetime;
            _capacity = capacity;
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _entries = new Dictionary<string, CacheEntry>(comparer ?? StringComparer.Ordinal);
        }

        public bool TryGet(string key, out T value)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            lock (_sync)
            {
                var now = _clock();
                if (_entries.TryGetValue(key, out var entry))
                {
                    if (entry.ExpiresAt > now)
                    {
                        value = entry.Value;
                        return true;
                    }

                    _entries.Remove(key);
                }
            }

            value = default!;
            return false;
        }

        public void Set(string key, T value)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            lock (_sync)
            {
                var now = _clock();
                RemoveExpired(now);

                if (!_entries.ContainsKey(key) && _entries.Count >= _capacity)
                {
                    var oldestKey = _entries
                        .OrderBy(pair => pair.Value.CreatedAt)
                        .Select(pair => pair.Key)
                        .First();
                    _entries.Remove(oldestKey);
                }

                _entries[key] = new CacheEntry(value, now, now.Add(_lifetime));
            }
        }

        internal int Count
        {
            get
            {
                lock (_sync)
                {
                    RemoveExpired(_clock());
                    return _entries.Count;
                }
            }
        }

        private void RemoveExpired(DateTimeOffset now)
        {
            var expiredKeys = _entries
                .Where(pair => pair.Value.ExpiresAt <= now)
                .Select(pair => pair.Key)
                .ToArray();

            foreach (var key in expiredKeys)
            {
                _entries.Remove(key);
            }
        }

        private sealed class CacheEntry
        {
            public CacheEntry(T value, DateTimeOffset createdAt, DateTimeOffset expiresAt)
            {
                Value = value;
                CreatedAt = createdAt;
                ExpiresAt = expiresAt;
            }

            public T Value { get; }

            public DateTimeOffset CreatedAt { get; }

            public DateTimeOffset ExpiresAt { get; }
        }
    }
}
