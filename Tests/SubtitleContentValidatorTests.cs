using System.Text;
using Jellyfin.Plugin.ThunderCat.Utils;

namespace Jellyfin.Plugin.ThunderCat.Tests;

public sealed class SubtitleContentValidatorTests
{
    [Theory]
    [InlineData("1\n00:00:01,000 --> 00:00:03,000\nHello\n")]
    [InlineData("WEBVTT\n\n00:01.000 --> 00:03.000\nHello\n")]
    [InlineData("[Script Info]\nTitle: Example\n")]
    public void IsSupportedText_AcceptsSubtitleText(string content)
    {
        Assert.True(SubtitleContentValidator.IsSupportedText(Encoding.UTF8.GetBytes(content)));
    }

    [Fact]
    public void IsSupportedText_AcceptsUtf16SubtitleText()
    {
        var preamble = Encoding.Unicode.GetPreamble();
        var content = Encoding.Unicode.GetBytes("1\r\n00:00:01,000 --> 00:00:02,000\r\n你好\r\n");
        var bytes = preamble.Concat(content).ToArray();

        Assert.True(SubtitleContentValidator.IsSupportedText(bytes));
    }

    [Theory]
    [InlineData("<!doctype html><html><body>blocked</body></html>")]
    [InlineData("{\"error\":\"not found\"}")]
    public void IsSupportedText_RejectsUpstreamErrorDocuments(string content)
    {
        Assert.False(SubtitleContentValidator.IsSupportedText(Encoding.UTF8.GetBytes(content)));
    }

    [Fact]
    public void IsSupportedText_RejectsArchives()
    {
        Assert.False(SubtitleContentValidator.IsSupportedText(new byte[] { 0x50, 0x4b, 0x03, 0x04, 0x00 }));
    }

    [Fact]
    public void Inspect_RejectsUnicodeReplacementCharacters()
    {
        var bytes = Encoding.UTF8.GetBytes(
            "1\n00:00:01,000 --> 00:00:02,000\n\uFFFD\uFFFD\uFFFD\n");

        var inspection = SubtitleContentValidator.Inspect(bytes);

        Assert.False(inspection.IsSupportedText);
        Assert.Equal("replacement-characters", inspection.RejectionReason);
        Assert.Equal(3, inspection.ReplacementCharacterCount);
    }

    [Theory]
    [InlineData("1\n00:00:01,000 --> 00:00:02,000\n锟斤拷\n")]
    [InlineData("1\n00:00:01,000 --> 00:00:02,000\n嚙踝蕭\n")]
    public void Inspect_RejectsKnownChineseMojibakeMarkers(string content)
    {
        var inspection = SubtitleContentValidator.Inspect(Encoding.UTF8.GetBytes(content));

        Assert.False(inspection.IsSupportedText);
        Assert.Equal("mojibake-markers", inspection.RejectionReason);
        Assert.True(inspection.MojibakeMarkerCount > 0);
    }
}
