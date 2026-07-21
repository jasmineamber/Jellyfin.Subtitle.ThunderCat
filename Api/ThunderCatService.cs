using System.Threading;
using System.Threading.Tasks;
using Emby.Subtitle.ThunderCat.Infrastructure;
using MediaBrowser.Common;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;

namespace Emby.Subtitle.ThunderCat.Api
{
    [Authenticated(Roles = "Admin")]
    [Route("/ThunderCat/Info", "GET", Summary = "Gets ThunderCat runtime information", IsHidden = true)]
    public sealed class GetThunderCatInfo : IReturn<ThunderCatInfoResponse>
    {
    }

    [Authenticated(Roles = "Admin")]
    [Route("/ThunderCat/Health", "GET", Summary = "Tests a ThunderCat subtitle provider", IsHidden = true)]
    public sealed class GetThunderCatHealth : IReturn<ThunderCatHealthResponse>
    {
        [ApiMember(Name = "Provider", ParameterType = "Query", IsRequired = true)]
        public string Provider { get; set; } = string.Empty;
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

    public sealed class ThunderCatService : IService
    {
        private readonly IApplicationHost _applicationHost;
        private readonly ILogger _logger;

        public ThunderCatService(IApplicationHost applicationHost, ILogManager logManager)
        {
            _applicationHost = applicationHost;
            _logger = logManager.GetLogger("ThunderCat");
        }

        public object Get(GetThunderCatInfo request)
        {
            var configuration = Plugin.Instance?.Configuration;
            return new ThunderCatInfoResponse
            {
                PluginVersion = PluginMetadata.Version,
                ServerVersion = _applicationHost.ApplicationVersion.ToString(),
                CompatibilityBaseline = PluginMetadata.CompatibilityBaseline,
                EnableSubtitleCat = configuration?.EnableSubtitleCat ?? true,
                EnableThunder = configuration?.EnableThunder ?? true
            };
        }

        public async Task<object> Get(GetThunderCatHealth request)
        {
            var result = await ProviderHealthChecker.CheckAsync(request.Provider, CancellationToken.None)
                .ConfigureAwait(false);
            if (!result.IsAvailable)
            {
                _logger.Warn($"[ThunderCat] Provider health check failed: {result.Provider} ({result.Status}).");
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
}
