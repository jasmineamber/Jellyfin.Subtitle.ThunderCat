using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ThunderCat.Infrastructure;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThunderCat.Api;

[ApiController]
[Authorize(Policy = Policies.SubtitleManagement)]
[Route("ThunderCat")]
public sealed class ThunderCatController : ControllerBase
{
    private readonly IServerApplicationHost _applicationHost;
    private readonly ILogger<ThunderCatController> _logger;

    public ThunderCatController(
        IServerApplicationHost applicationHost,
        ILogger<ThunderCatController> logger)
    {
        _applicationHost = applicationHost;
        _logger = logger;
    }

    [HttpGet("Info")]
    public ActionResult<ThunderCatInfoResponse> GetInfo()
    {
        var configuration = ThunderCatPlugin.Instance?.Configuration;
        return new ThunderCatInfoResponse
        {
            PluginVersion = PluginMetadata.Version,
            ServerVersion = _applicationHost.ApplicationVersionString,
            CompatibilityBaseline = PluginMetadata.CompatibilityBaseline,
            EnableSubtitleCat = configuration?.EnableSubtitleCat ?? true,
            EnableThunder = configuration?.EnableThunder ?? true
        };
    }

    [HttpGet("Health")]
    public async Task<ActionResult<ThunderCatHealthResponse>> GetHealth(
        [FromQuery] string provider,
        CancellationToken cancellationToken)
    {
        var result = await ProviderHealthChecker
            .CheckAsync(provider, cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsAvailable)
        {
            _logger.LogWarning(
                "[ThunderCat] Provider health check failed: {Provider} ({Status}).",
                result.Provider,
                result.Status);
        }

        return new ThunderCatHealthResponse
        {
            Provider = result.Provider,
            IsAvailable = result.IsAvailable,
            Status = result.Status,
            ElapsedMilliseconds = result.ElapsedMilliseconds
        };
    }
}

public sealed class ThunderCatInfoResponse
{
    public string PluginVersion { get; set; } = string.Empty;

    public string ServerVersion { get; set; } = string.Empty;

    public string CompatibilityBaseline { get; set; } = string.Empty;

    public bool EnableSubtitleCat { get; set; }

    public bool EnableThunder { get; set; }
}

public sealed class ThunderCatHealthResponse
{
    public string Provider { get; set; } = string.Empty;

    public bool IsAvailable { get; set; }

    public string Status { get; set; } = string.Empty;

    public long ElapsedMilliseconds { get; set; }
}
