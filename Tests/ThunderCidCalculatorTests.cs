using System.Security.Cryptography;
using Emby.Subtitle.ThunderCat.Utils;

namespace Emby.Subtitle.ThunderCat.Tests;

public sealed class ThunderCidCalculatorTests
{
    [Fact]
    public async Task CalculateAsync_HashesEntireSmallFile()
    {
        var bytes = Enumerable.Range(0, 4096).Select(i => (byte)(i % 251)).ToArray();
        var path = await WriteTempFileAsync(bytes);
        try
        {
            var actual = await ThunderCidCalculator.CalculateAsync(path, CancellationToken.None);
            Assert.Equal(ToHex(SHA1.HashData(bytes)), actual);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CalculateAsync_HashesThreeSegmentsForLargeFile()
    {
        const int segmentSize = 0x5000;
        var bytes = Enumerable.Range(0, 100_000).Select(i => (byte)(i % 239)).ToArray();
        var path = await WriteTempFileAsync(bytes);
        try
        {
            var selected = bytes.Take(segmentSize)
                .Concat(bytes.Skip(bytes.Length / 3).Take(segmentSize))
                .Concat(bytes.Skip(bytes.Length - segmentSize).Take(segmentSize))
                .ToArray();

            var actual = await ThunderCidCalculator.CalculateAsync(path, CancellationToken.None);
            Assert.Equal(ToHex(SHA1.HashData(selected)), actual);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CalculateAsync_RecomputesWhenFileMetadataChanges()
    {
        var path = await WriteTempFileAsync(new byte[] { 1, 2, 3 });
        try
        {
            var first = await ThunderCidCalculator.CalculateAsync(path, CancellationToken.None);
            await File.WriteAllBytesAsync(path, new byte[] { 4, 5, 6, 7 });
            var second = await ThunderCidCalculator.CalculateAsync(path, CancellationToken.None);

            Assert.NotEqual(first, second);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CalculateAsync_HandlesEmptyFile()
    {
        var path = await WriteTempFileAsync(Array.Empty<byte>());
        try
        {
            var actual = await ThunderCidCalculator.CalculateAsync(path, CancellationToken.None);
            Assert.Equal("DA39A3EE5E6B4B0D3255BFEF95601890AFD80709", actual);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadExactlyAsync_HandlesPartialReads()
    {
        var source = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
        using var stream = new PartialReadStream(source, 3);
        var destination = new byte[source.Length];

        await ThunderCidCalculator.ReadExactlyAsync(
            stream,
            destination,
            0,
            destination.Length,
            CancellationToken.None);

        Assert.Equal(source, destination);
    }


    [Fact]
    public async Task ReadExactlyAsync_PropagatesCancellation()
    {
        using var stream = new PartialReadStream(new byte[64], 1);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ThunderCidCalculator.ReadExactlyAsync(stream, new byte[64], 0, 64, source.Token));
    }

    [Fact]
    public async Task ReadExactlyAsync_ThrowsWhenStreamEndsEarly()
    {
        using var stream = new PartialReadStream(new byte[5], 2);
        await Assert.ThrowsAsync<EndOfStreamException>(() =>
            ThunderCidCalculator.ReadExactlyAsync(stream, new byte[10], 0, 10, CancellationToken.None));
    }

    private static async Task<string> WriteTempFileAsync(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "thundercat-" + Guid.NewGuid().ToString("N") + ".bin");
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }

    private static string ToHex(byte[] bytes) => Convert.ToHexString(bytes);

    private sealed class PartialReadStream : MemoryStream
    {
        private readonly int _maxRead;

        public PartialReadStream(byte[] bytes, int maxRead)
            : base(bytes, false)
        {
            _maxRead = maxRead;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            base.ReadAsync(buffer, offset, Math.Min(count, _maxRead), cancellationToken);
    }
}
