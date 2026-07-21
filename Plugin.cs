using System;
using System.Collections.Generic;
using Emby.Subtitle.ThunderCat.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Emby.Subtitle.ThunderCat
{
    public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        private static readonly Guid PluginId = new Guid("9b7dca24-edf8-4155-bbd6-8c2b0f07448d");

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public static Plugin Instance { get; private set; } = null!;

        public override string Name => "ThunderCat Subtitles";

        public override Guid Id => PluginId;

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = Name,
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
