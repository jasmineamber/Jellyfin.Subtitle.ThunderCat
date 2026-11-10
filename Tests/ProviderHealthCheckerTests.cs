using Jellyfin.Plugin.ThunderCat.Infrastructure;

namespace Jellyfin.Plugin.ThunderCat.Tests;

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
        Assert.Equal("2.0.2", PluginMetadata.Version);
        Assert.Contains("ThunderCat/2.0.2", PluginMetadata.UserAgent, StringComparison.Ordinal);
        Assert.Contains(
            "https://github.com/jasmineamber/Jellyfin.Subtitle.ThunderCat",
            PluginMetadata.UserAgent,
            StringComparison.Ordinal);
        Assert.Equal("Jellyfin Server 10.10.7", PluginMetadata.CompatibilityBaseline);
    }

    [Fact]
    public void ApiRoutes_RequireSubtitleManagementAndExposeInfoAndHealth()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "Api",
            "ThunderCatController.cs"));
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("[ApiController]", source, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Policy = Policies.SubtitleManagement)]", source, StringComparison.Ordinal);
        Assert.Contains("[Route(\"ThunderCat\")]", source, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"Info\")]", source, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"Health\")]", source, StringComparison.Ordinal);
    }

}
