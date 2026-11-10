using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.ThunderCat.Utils
{
    internal static class SubtitleContentNormalizer
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
        private static readonly Regex SrtTimestampRegex = new Regex(
            @"^\s*\d{1,3}:\d{2}:\d{2}[,.]\d{1,3}\s*-->\s*\d{1,3}:\d{2}:\d{2}[,.]\d{1,3}",
            RegexOptions.CultureInvariant,
            RegexTimeout);

        public static byte[] Normalize(byte[] bytes, string? format)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            if (!string.Equals(format, "srt", StringComparison.OrdinalIgnoreCase) || bytes.Length == 0)
            {
                return bytes;
            }

            var encoding = DetectEncoding(bytes, out var preambleLength);
            var text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
            var firstContent = text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            if (!SrtTimestampRegex.IsMatch(firstContent))
            {
                return bytes;
            }

            var newline = text.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            var prefix = encoding.GetBytes("1" + newline);
            var result = new byte[preambleLength + prefix.Length + bytes.Length - preambleLength];
            Buffer.BlockCopy(bytes, 0, result, 0, preambleLength);
            Buffer.BlockCopy(prefix, 0, result, preambleLength, prefix.Length);
            Buffer.BlockCopy(
                bytes,
                preambleLength,
                result,
                preambleLength + prefix.Length,
                bytes.Length - preambleLength);
            return result;
        }

        private static Encoding DetectEncoding(byte[] bytes, out int preambleLength)
        {
            if (bytes.Length >= 3 &&
                bytes[0] == 0xef &&
                bytes[1] == 0xbb &&
                bytes[2] == 0xbf)
            {
                preambleLength = 3;
                return new UTF8Encoding(false);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe)
            {
                preambleLength = 2;
                return Encoding.Unicode;
            }

            if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff)
            {
                preambleLength = 2;
                return Encoding.BigEndianUnicode;
            }

            preambleLength = 0;
            return new UTF8Encoding(false);
        }
    }
}
