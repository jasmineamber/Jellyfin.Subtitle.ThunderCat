using Jellyfin.Plugin.ThunderCat.Providers;
using MediaBrowser.Controller.Subtitles;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.ThunderCat.Tests;

public sealed class PluginIntegrationTests
{
    [Fact]
    public void ServiceRegistrator_RegistersBothSubtitleProviders()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new PluginServiceRegistrator().RegisterServices(services, null!);

        using var provider = services.BuildServiceProvider();
        var subtitleProviders = provider.GetServices<ISubtitleProvider>().ToArray();

        Assert.Equal(2, subtitleProviders.Length);
        Assert.Contains(subtitleProviders, item => item is SubtitleCatProvider);
        Assert.Contains(subtitleProviders, item => item is ThunderProvider);
    }

    [Fact]
    public void Manifest_TargetsTheJellyfinRuntime()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var manifest = File.ReadAllText(Path.Combine(root, "build.yaml"));
        var project = File.ReadAllText(Path.Combine(root, "Jellyfin.Plugin.ThunderCat.csproj"));
        var plugin = File.ReadAllText(Path.Combine(root, "Plugin.cs"));

        Assert.Contains("guid: \"9b7dca24-edf8-4155-bbd6-8c2b0f07448d\"", manifest, StringComparison.Ordinal);
        Assert.Contains("version: 4", manifest, StringComparison.Ordinal);
        Assert.Contains("targetAbi: \"10.10.7.0\"", manifest, StringComparison.Ordinal);
        Assert.Contains("framework: \"net8.0\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"Jellyfin.Plugin.ThunderCat.dll\"", manifest, StringComparison.Ordinal);
        Assert.Contains("<TargetFramework>net8.0</TargetFramework>", project, StringComparison.Ordinal);
        Assert.Contains("<Version>2.0.2</Version>", project, StringComparison.Ordinal);
        Assert.Contains(
            "<PackageProjectUrl>https://github.com/jasmineamber/Jellyfin.Subtitle.ThunderCat</PackageProjectUrl>",
            project,
            StringComparison.Ordinal);
        Assert.Contains("<JellyfinVersion Condition=\"'$(JellyfinVersion)' == ''\">10.10.7</JellyfinVersion>", project, StringComparison.Ordinal);
        Assert.Contains("new Guid(\"9b7dca24-edf8-4155-bbd6-8c2b0f07448d\")", plugin, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflow_PublishesTaggedPackagesToDistManifest()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));

        Assert.Contains("tags:", workflow, StringComparison.Ordinal);
        Assert.Contains("contents: write", workflow, StringComparison.Ordinal);
        Assert.Contains("scripts/generate_manifest.py", workflow, StringComparison.Ordinal);
        Assert.Contains("origin/dist", workflow, StringComparison.Ordinal);
        Assert.Contains("manifest.json", workflow, StringComparison.Ordinal);
        Assert.Contains("--warnaserror", workflow, StringComparison.Ordinal);
    }
}
