using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ThunderCat.Configuration
{
    public sealed class PluginConfiguration : BasePluginConfiguration
    {
        public bool EnableSubtitleCat { get; set; } = true;

        public bool EnableThunder { get; set; } = true;
    }
}
