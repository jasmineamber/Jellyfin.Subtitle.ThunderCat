using System;

namespace Emby.Subtitle.ThunderCat.Infrastructure
{
    internal static class PluginMetadata
    {
        public const string CompatibilityBaseline = "Emby Server 4.8.11.0";

        public static string Version
        {
            get
            {
                var version = typeof(PluginMetadata).Assembly.GetName().Version;
                return version == null ? "unknown" : version.ToString(3);
            }
        }

        public static string UserAgent =>
            "ThunderCat/" + Version + " (+https://github.com/drunkleee/Emby.Subtitle.ThunderCat)";
    }
}
