using System;

namespace Jellyfin.Plugin.ThunderCat.Infrastructure
{
    internal static class PluginMetadata
    {
        public const string CompatibilityBaseline = "Jellyfin Server 10.10.7";

        public static string Version
        {
            get
            {
                var version = typeof(PluginMetadata).Assembly.GetName().Version;
                return version == null ? "unknown" : version.ToString(3);
            }
        }

        public static string UserAgent =>
            "ThunderCat/" + Version + " (+https://github.com/jasmineamber/Jellyfin.Subtitle.ThunderCat)";
    }
}
