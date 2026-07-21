using MediaBrowser.Model.Plugins;

namespace Emby.Subtitle.ThunderCat.Configuration
{
    public sealed class PluginConfiguration : BasePluginConfiguration
    {
        public bool EnableSubtitleCat { get; set; } = true;

        public bool EnableThunder { get; set; } = true;
    }
}
