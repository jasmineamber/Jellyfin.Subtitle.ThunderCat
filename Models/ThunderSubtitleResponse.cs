using System.Collections.Generic;

namespace Jellyfin.Plugin.ThunderCat.Models
{
    public sealed class ThunderSubtitleResponse
    {
        public int Code { get; set; }

        public List<ThunderSubtitleItem>? Data { get; set; }

        public string? Result { get; set; }
    }

    public sealed class ThunderSubtitleItem
    {
        public string? Gcid { get; set; }

        public string? Cid { get; set; }

        public string? Url { get; set; }

        public string? Ext { get; set; }

        public string? Name { get; set; }

        public long Duration { get; set; }

        public string[]? Languages { get; set; }

        public int Source { get; set; }

        public int Score { get; set; }
    }
}
