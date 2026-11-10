using System.Net;
using System.Security;
using Jellyfin.Plugin.ThunderCat.Infrastructure;

namespace Jellyfin.Plugin.ThunderCat.Tests;

public sealed class UrlSafetyValidatorTests
{
    private static readonly UrlPolicy Policy = new UrlPolicy("subtitlecat.com");

    [Theory]
    [InlineData("http://subtitlecat.com/sub.srt")]
    [InlineData("https://example.com/sub.srt")]
    [InlineData("https://subtitlecat.com:444/sub.srt")]
    [InlineData("https://user:pass@subtitlecat.com/sub.srt")]
    public void ValidateSyntax_RejectsUnsafeUrls(string value)
    {
        Assert.Throws<SecurityException>(() => UrlSafetyValidator.ValidateSyntax(new Uri(value), Policy));
    }

    [Fact]
    public void ValidateSyntax_AcceptsAllowlistedHttpsUrl()
    {
        UrlSafetyValidator.ValidateSyntax(new Uri("https://subtitlecat.com/subs/1/test.srt"), Policy);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    public void IsPublicAddress_RejectsNonPublicAddresses(string value)
    {
        Assert.False(UrlSafetyValidator.IsPublicAddress(IPAddress.Parse(value)));
    }

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("2606:4700:4700::1111")]
    public void IsPublicAddress_AcceptsPublicAddresses(string value)
    {
        Assert.True(UrlSafetyValidator.IsPublicAddress(IPAddress.Parse(value)));
    }
}
