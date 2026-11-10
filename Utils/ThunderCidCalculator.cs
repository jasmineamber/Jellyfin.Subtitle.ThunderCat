using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.ThunderCat.Utils
{
    internal static class ThunderCidCalculator
    {
        private const int SegmentSize = 0x5000;
        private const int CombinedSize = SegmentSize * 3;
        private const int MaxCacheEntries = 256;
        private static readonly ConcurrentDictionary<string, CacheEntry> Cache =
            new ConcurrentDictionary<string, CacheEntry>(
                Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        public static async Task<string> CalculateAsync(string filePath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A media file path is required.", nameof(filePath));
            }

            var fullPath = Path.GetFullPath(filePath);
            var before = new FileInfo(fullPath);
            if (!before.Exists)
            {
                throw new FileNotFoundException("The media file does not exist.", fullPath);
            }

            if (Cache.TryGetValue(fullPath, out var cached) && cached.Matches(before))
            {
                return cached.Cid;
            }

            var cid = await CalculateCoreAsync(fullPath, cancellationToken).ConfigureAwait(false);
            var after = new FileInfo(fullPath);
            after.Refresh();
            if (!after.Exists || before.Length != after.Length || before.LastWriteTimeUtc != after.LastWriteTimeUtc)
            {
                throw new IOException("The media file changed while calculating its CID.");
            }

            Cache[fullPath] = new CacheEntry(after.Length, after.LastWriteTimeUtc, cid, DateTime.UtcNow.Ticks);
            PruneCache();
            return cid;
        }

        internal static async Task ReadExactlyAsync(
            Stream stream,
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            var totalRead = 0;
            while (totalRead < count)
            {
                var read = await stream.ReadAsync(
                        buffer,
                        offset + totalRead,
                        count - totalRead,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    throw new EndOfStreamException("The media file changed or ended while calculating its CID.");
                }

                totalRead += read;
            }
        }

        private static async Task<string> CalculateCoreAsync(string filePath, CancellationToken cancellationToken)
        {
            using (var stream = new FileStream(
                       filePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       4096,
                       FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var fileSize = stream.Length;
                byte[] bytesToHash;

                if (fileSize < CombinedSize)
                {
                    if (fileSize > int.MaxValue)
                    {
                        throw new IOException("The media file is too large for the small-file CID path.");
                    }

                    bytesToHash = new byte[(int)fileSize];
                    await ReadExactlyAsync(stream, bytesToHash, 0, bytesToHash.Length, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    bytesToHash = new byte[CombinedSize];

                    stream.Seek(0, SeekOrigin.Begin);
                    await ReadExactlyAsync(stream, bytesToHash, 0, SegmentSize, cancellationToken).ConfigureAwait(false);

                    stream.Seek(fileSize / 3, SeekOrigin.Begin);
                    await ReadExactlyAsync(stream, bytesToHash, SegmentSize, SegmentSize, cancellationToken)
                        .ConfigureAwait(false);

                    stream.Seek(fileSize - SegmentSize, SeekOrigin.Begin);
                    await ReadExactlyAsync(stream, bytesToHash, SegmentSize * 2, SegmentSize, cancellationToken)
                        .ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CA5350 // Thunder CID is a compatibility hash mandated by the upstream protocol, not a security primitive.
                using (var sha1 = SHA1.Create())
                {
                    return ToUpperHex(sha1.ComputeHash(bytesToHash));
                }
#pragma warning restore CA5350
            }
        }

        private static void PruneCache()
        {
            var overflow = Cache.Count - MaxCacheEntries;
            if (overflow <= 0)
            {
                return;
            }

            foreach (var entry in Cache.OrderBy(pair => pair.Value.CachedAtTicks).Take(overflow))
            {
                Cache.TryRemove(entry.Key, out _);
            }
        }

        private static string ToUpperHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes)
            {
                builder.Append(value.ToString("X2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private sealed class CacheEntry
        {
            public CacheEntry(long length, DateTime lastWriteTimeUtc, string cid, long cachedAtTicks)
            {
                Length = length;
                LastWriteTimeUtc = lastWriteTimeUtc;
                Cid = cid;
                CachedAtTicks = cachedAtTicks;
            }

            public long Length { get; }

            public DateTime LastWriteTimeUtc { get; }

            public string Cid { get; }

            public long CachedAtTicks { get; }

            public bool Matches(FileInfo file) =>
                file.Exists && file.Length == Length && file.LastWriteTimeUtc == LastWriteTimeUtc;
        }
    }
}
