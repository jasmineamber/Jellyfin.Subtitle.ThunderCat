using System.Text;
using Jellyfin.Plugin.ThunderCat.Utils;

namespace Jellyfin.Plugin.ThunderCat.Tests;

public sealed class SubtitleContentNormalizerTests
{
    [Fact]
    public void Normalize_PrependsMissingFirstSrtCueIndex()
    {
        const string content = "00:00:01,000 --> 00:00:02,000\r\nHello\r\n";

        var normalized = SubtitleContentNormalizer.Normalize(
            Encoding.UTF8.GetBytes(content),
            "srt");

        Assert.Equal("1\r\n" + content, Encoding.UTF8.GetString(normalized));
    }

    [Fact]
    public void Normalize_PreservesUtf16PreambleWhenRepairingSrt()
    {
        const string content = "00:00:01,000 --> 00:00:02,000\r\n你好\r\n";
        var bytes = Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes(content))
            .ToArray();

        var normalized = SubtitleContentNormalizer.Normalize(bytes, "srt");

        Assert.Equal(Encoding.Unicode.GetPreamble(), normalized.Take(2));
        Assert.Equal(
            "1\r\n" + content,
            Encoding.Unicode.GetString(normalized, 2, normalized.Length - 2));
    }

    [Fact]
    public void Normalize_LeavesValidSrtAndOtherFormatsUnchanged()
    {
        var validSrt = Encoding.UTF8.GetBytes(
            "1\r\n00:00:01,000 --> 00:00:02,000\r\nHello\r\n");
        var webVtt = Encoding.UTF8.GetBytes(
            "WEBVTT\r\n\r\n00:00:01.000 --> 00:00:02.000\r\nHello\r\n");

        Assert.Same(validSrt, SubtitleContentNormalizer.Normalize(validSrt, "srt"));
        Assert.Same(webVtt, SubtitleContentNormalizer.Normalize(webVtt, "vtt"));
    }
}
