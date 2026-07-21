using System;
using System.Text;

namespace Emby.Subtitle.ThunderCat.Utils
{
    internal static class SubtitleContentValidator
    {
        private static readonly byte[][] ArchiveSignatures =
        {
            new byte[] { 0x50, 0x4b, 0x03, 0x04 },
            new byte[] { 0x52, 0x61, 0x72, 0x21 },
            new byte[] { 0x37, 0x7a, 0xbc, 0xaf },
            new byte[] { 0x1f, 0x8b }
        };

        public static bool IsSupportedText(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return false;
            }

            foreach (var signature in ArchiveSignatures)
            {
                if (StartsWith(bytes, signature))
                {
                    return false;
                }
            }

            var text = DecodePrefix(bytes, 4096).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            if (text.Length == 0)
            {
                return false;
            }

            var lower = text.ToLowerInvariant();
            if (lower.StartsWith("<!doctype html", StringComparison.Ordinal) ||
                lower.StartsWith("<html", StringComparison.Ordinal) ||
                lower.StartsWith("<head", StringComparison.Ordinal) ||
                lower.StartsWith("<body", StringComparison.Ordinal) ||
                lower.StartsWith("<?xml", StringComparison.Ordinal) ||
                lower.StartsWith("{\"code\"", StringComparison.Ordinal) ||
                lower.StartsWith("{\"error\"", StringComparison.Ordinal))
            {
                return false;
            }

            return HasAcceptableControlCharacterRatio(text);
        }

        private static string DecodePrefix(byte[] bytes, int maxBytes)
        {
            var count = Math.Min(bytes.Length, maxBytes);
            if (count >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe)
            {
                count -= count % 2;
                return Encoding.Unicode.GetString(bytes, 0, count);
            }

            if (count >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff)
            {
                count -= count % 2;
                return Encoding.BigEndianUnicode.GetString(bytes, 0, count);
            }

            return Encoding.UTF8.GetString(bytes, 0, count);
        }

        private static bool HasAcceptableControlCharacterRatio(string text)
        {
            var controls = 0;
            foreach (var character in text)
            {
                if (char.IsControl(character) && character != '\r' && character != '\n' && character != '\t')
                {
                    controls++;
                }
            }

            return controls * 100 <= text.Length;
        }

        private static bool StartsWith(byte[] bytes, byte[] prefix)
        {
            if (bytes.Length < prefix.Length)
            {
                return false;
            }

            for (var index = 0; index < prefix.Length; index++)
            {
                if (bytes[index] != prefix[index])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
