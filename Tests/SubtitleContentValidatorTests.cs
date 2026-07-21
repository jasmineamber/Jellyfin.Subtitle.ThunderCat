using System.Text;
using Emby.Subtitle.ThunderCat.Utils;

namespace Emby.Subtitle.ThunderCat.Tests;

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
}
