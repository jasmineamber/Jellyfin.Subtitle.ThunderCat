using Jellyfin.Plugin.ThunderCat.Infrastructure;

namespace Jellyfin.Plugin.ThunderCat.Tests;

public sealed class DownloadTokenStoreTests
{
    [Fact]
    public void Token_IsOpaqueAndBoundToProvider()
    {
        var store = new DownloadTokenStore();
        var uri = new Uri("https://subtitle.v.geilijiasu.com/AA/BB/test.srt");
        var fallbackUri = new Uri("https://subtitlecat.com/subs/2/fallback.html");
        var token = store.Create(new DownloadRequest(
            "Thunder",
            uri,
            "chi",
            "srt",
            candidateUris: new[] { uri, fallbackUri },
            displayName: "[ID] Test",
            mediaCode: "TEST-001",
            isAutomated: true));

        Assert.DoesNotContain("geilijiasu", token, StringComparison.OrdinalIgnoreCase);
        Assert.True(store.TryResolve(token, "Thunder", out var request));
        Assert.Equal(uri, request.Uri);
        Assert.Equal(new[] { uri, fallbackUri }, request.CandidateUris);
        Assert.Equal("[ID] Test", request.DisplayName);
        Assert.Equal("TEST-001", request.MediaCode);
        Assert.True(request.IsAutomated);
        Assert.False(store.TryResolve(token, "SubtitleCat", out _));
        Assert.False(store.TryResolve(token + "x", "Thunder", out _));
    }

    [Theory]
    [InlineData("https://subtitle.v.geilijiasu.com/test.srt")]
    [InlineData("aHR0cHM6Ly9zdWJ0aXRsZS52LmdlaWxpag==")]
    [InlineData("")]
    public void RawOrMalformedIdsAreRejected(string token)
    {
        var store = new DownloadTokenStore();
        Assert.False(store.TryResolve(token, "Thunder", out _));
    }
}
