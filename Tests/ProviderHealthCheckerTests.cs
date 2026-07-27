using Emby.Subtitle.ThunderCat.Infrastructure;

namespace Emby.Subtitle.ThunderCat.Tests;

public sealed class ProviderHealthCheckerTests
{
    [Theory]
    [InlineData("SubtitleCat", "subtitlecat.com")]
    [InlineData("subtitlecat", "subtitlecat.com")]
    [InlineData("Thunder", "api-shoulei-ssl.xunlei.com")]
    [InlineData("thunder", "api-shoulei-ssl.xunlei.com")]
    public void TryGetProbeDefinition_AllowsOnlyKnownProviders(string provider, string expectedHost)
    {
        Assert.True(ProviderHealthChecker.TryGetProbeDefinition(provider, out var definition));
        Assert.Equal(expectedHost, definition.Uri.Host);
        UrlSafetyValidator.ValidateSyntax(definition.Uri, definition.Policy);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unknown")]
    [InlineData("https://example.com/")]
    [InlineData("Thunder?url=https://example.com/")]
    public void TryGetProbeDefinition_RejectsArbitraryTargets(string provider)
    {
        Assert.False(ProviderHealthChecker.TryGetProbeDefinition(provider, out _));
    }

    [Fact]
    public void PluginMetadata_DerivesVersionAndUserAgentFromAssembly()
    {
        Assert.Equal("1.4.0", PluginMetadata.Version);
        Assert.Contains("ThunderCat/1.4.0", PluginMetadata.UserAgent, StringComparison.Ordinal);
        Assert.Equal("Emby Server 4.8.11.0", PluginMetadata.CompatibilityBaseline);
    }

    [Fact]
    public void ApiRoutes_AreHiddenAdminOnlyGetEndpoints()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "Api",
            "ThunderCatService.cs"));
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("[Authenticated(Roles = \"Admin\")]", source, StringComparison.Ordinal);
        Assert.Contains("[Route(\"/ThunderCat/Info\", \"GET\"", source, StringComparison.Ordinal);
        Assert.Contains("[Route(\"/ThunderCat/Health\", \"GET\"", source, StringComparison.Ordinal);
        Assert.Contains("IsHidden = true", source, StringComparison.Ordinal);
        Assert.Contains("IReturn<ThunderCatInfoResponse>", source, StringComparison.Ordinal);
        Assert.Contains("IReturn<ThunderCatHealthResponse>", source, StringComparison.Ordinal);
    }

}
