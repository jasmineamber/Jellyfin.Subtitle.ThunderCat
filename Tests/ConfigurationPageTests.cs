using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.ThunderCat.Tests;

public sealed class ConfigurationPageTests
{
    private static readonly string ProjectRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static readonly string PagePath = Path.Combine(ProjectRoot, "Configuration", "configPage.html");
    private static readonly string ControllerPath = Path.Combine(ProjectRoot, "Configuration", "configPage.js");
    private static readonly string PluginPath = Path.Combine(ProjectRoot, "Plugin.cs");
    private static readonly string ProjectPath = Path.Combine(ProjectRoot, "Jellyfin.Plugin.ThunderCat.csproj");

    [Fact]
    public void ConfigurationPage_UsesEmbeddedJellyfinModuleController()
    {
        var html = File.ReadAllText(PagePath);
        var controller = File.ReadAllText(ControllerPath);
        var plugin = File.ReadAllText(PluginPath);
        var project = File.ReadAllText(ProjectPath);
        var match = Regex.Match(html, "data-controller=\"__plugin/(?<name>[^\"]+)\"");

        Assert.True(match.Success);
        Assert.Equal("thundercatconfigjs", match.Groups["name"].Value);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("export default function (view)", controller, StringComparison.Ordinal);
        Assert.Contains("Dashboard.showLoadingMsg()", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("define(", controller, StringComparison.Ordinal);
        Assert.Contains($"Name = \"{match.Groups["name"].Value}\"", plugin, StringComparison.Ordinal);
        Assert.Contains(".Configuration.configPage.js", plugin, StringComparison.Ordinal);
        Assert.Contains("EmbeddedResource Include=\"Configuration\\configPage.js\"", project, StringComparison.Ordinal);
        Assert.DoesNotContain("data-require=", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationPage_KeepsStylesInsideExtractedViewRoot()
    {
        var html = File.ReadAllText(PagePath);
        var rootIndex = html.IndexOf("<div data-role=\"page\"", StringComparison.Ordinal);
        var styleIndex = html.IndexOf("<style>", StringComparison.Ordinal);

        Assert.Equal(0, rootIndex);
        Assert.True(styleIndex > rootIndex);
        Assert.DoesNotContain("<html>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<head>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<body>", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfigurationController_UsesNativeViewLifecycle()
    {
        var controller = File.ReadAllText(ControllerPath);

        Assert.Contains("page.addEventListener('viewshow', loadConfiguration)", controller, StringComparison.Ordinal);
        Assert.Contains("form.addEventListener('submit'", controller, StringComparison.Ordinal);
        Assert.Contains("initializePage(view)", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("document.querySelectorAll('.thunderCatConfigurationPage')", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("window[controllerKey]", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationController_LoadsAndSavesBothProviderFlags()
    {
        var controller = File.ReadAllText(ControllerPath);

        Assert.Contains("ApiClient.getPluginConfiguration(pluginId)", controller, StringComparison.Ordinal);
        Assert.Contains("ApiClient.updatePluginConfiguration(pluginId, config)", controller, StringComparison.Ordinal);
        Assert.Contains("config.EnableSubtitleCat = subtitleCatCheckbox.checked", controller, StringComparison.Ordinal);
        Assert.Contains("config.EnableThunder = thunderCheckbox.checked", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationPage_ProvidesHealthChecksAndPrivacySafeDiagnostics()
    {
        var html = File.ReadAllText(PagePath);
        var controller = File.ReadAllText(ControllerPath);

        Assert.Contains("ThunderCat/Health", controller, StringComparison.Ordinal);
        Assert.Contains("ThunderCat/Info", controller, StringComparison.Ordinal);
        Assert.Contains("data-action=\"test-provider\"", html, StringComparison.Ordinal);
        Assert.Contains("data-action=\"copy-diagnostics\"", html, StringComparison.Ordinal);
        Assert.Contains("诊断信息不包含媒体路径、搜索词或下载地址", html, StringComparison.Ordinal);
        Assert.DoesNotContain("request.MediaPath", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationController_ShowsDirtyAndSavedStates()
    {
        var controller = File.ReadAllText(ControllerPath);

        Assert.Contains("有未保存的更改", controller, StringComparison.Ordinal);
        Assert.Contains("配置已保存", controller, StringComparison.Ordinal);
        Assert.Contains("saveButton.disabled = !isDirty", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationPage_UsesPlainMediaIdentifierWording()
    {
        var html = File.ReadAllText(PagePath);

        Assert.Contains("文件编号识别", html, StringComparison.Ordinal);
        Assert.Contains("额外的检索线索", html, StringComparison.Ordinal);
        Assert.DoesNotContain("内容" + "中立", html, StringComparison.Ordinal);
        Assert.DoesNotContain("不限定媒体" + "内容类别", html, StringComparison.Ordinal);
        Assert.DoesNotContain("番号", html, StringComparison.Ordinal);
        Assert.DoesNotContain("FC2-PPV", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LULU-", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SONE-", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfigurationPage_UsesResponsiveThemeAwareLayout()
    {
        var html = File.ReadAllText(PagePath);

        Assert.Contains("class=\"thundercat-layout\"", html, StringComparison.Ordinal);
        Assert.Contains(".theme-light .thunderCatConfigurationPage", html, StringComparison.Ordinal);
        Assert.Contains(".theme-dark .thunderCatConfigurationPage", html, StringComparison.Ordinal);
        Assert.Contains("flex-wrap: wrap", html, StringComparison.Ordinal);
        Assert.Contains("flex: 1.65 1 620px", html, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(0, 1fr) auto", html, StringComparison.Ordinal);
        Assert.Contains(".thunderCatConfigurationForm", html, StringComparison.Ordinal);
        Assert.Contains("max-width: none", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationPage_DoesNotHardcodePluginVersion()
    {
        var html = File.ReadAllText(PagePath);

        Assert.Contains("data-role=\"plugin-version\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Version 1.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("v1.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationController_ReportsWhenJellyfinApiNeverBecomesReady()
    {
        var controller = File.ReadAllText(ControllerPath);

        Assert.Contains("Jellyfin API 未就绪，请刷新页面", controller, StringComparison.Ordinal);
        Assert.Contains("setProviderButtonsDisabled(false)", controller, StringComparison.Ordinal);
    }
}
