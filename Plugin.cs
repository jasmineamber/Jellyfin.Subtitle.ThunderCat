using System;
using System.Collections.Generic;
using Jellyfin.Plugin.ThunderCat.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.ThunderCat
{
    public sealed class ThunderCatPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        private static readonly Guid PluginId = new Guid("9b7dca24-edf8-4155-bbd6-8c2b0f07448d");

        public ThunderCatPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public static ThunderCatPlugin Instance { get; private set; } = null!;

        public override string Name => "ThunderCat Subtitles";

        public override Guid Id => PluginId;

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "thundercat",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
                },
                new PluginPageInfo
                {
                    Name = "thundercatconfigjs",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.js"
                }
            };
        }
    }
}
