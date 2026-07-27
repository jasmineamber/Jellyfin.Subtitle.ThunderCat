using System;
using System.Collections.Generic;

namespace Emby.Subtitle.ThunderCat.Utils
{
    internal sealed class SubtitleResultDeduplicator
    {
        private readonly HashSet<string> _uris = new HashSet<string>(StringComparer.Ordinal);

        public bool TryAdd(Uri uri) => _uris.Add(uri.AbsoluteUri);
    }
}
