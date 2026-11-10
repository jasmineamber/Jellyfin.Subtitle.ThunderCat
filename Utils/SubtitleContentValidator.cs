using System;
using System.Text;

namespace Jellyfin.Plugin.ThunderCat.Utils
{
    internal sealed class SubtitleContentInspection
    {
        public SubtitleContentInspection(
            bool isSupportedText,
            string? rejectionReason,
            int replacementCharacterCount,
            int mojibakeMarkerCount)
        {
            IsSupportedText = isSupportedText;
            RejectionReason = rejectionReason;
            ReplacementCharacterCount = replacementCharacterCount;
            MojibakeMarkerCount = mojibakeMarkerCount;
        }

        public bool IsSupportedText { get; }

        public string? RejectionReason { get; }

        public int ReplacementCharacterCount { get; }

        public int MojibakeMarkerCount { get; }
    }

    internal static class SubtitleContentValidator
    {
        private static readonly string[] MojibakeMarkers =
        {
            "锟斤拷",
            "嚙踝蕭"
        };

        private static readonly byte[][] ArchiveSignatures =
        {
            new byte[] { 0x50, 0x4b, 0x03, 0x04 },
            new byte[] { 0x52, 0x61, 0x72, 0x21 },
            new byte[] { 0x37, 0x7a, 0xbc, 0xaf },
            new byte[] { 0x1f, 0x8b }
        };

        public static bool IsSupportedText(byte[] bytes)
        {
            return Inspect(bytes).IsSupportedText;
        }

        public static SubtitleContentInspection Inspect(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return Rejected("empty");
            }

            foreach (var signature in ArchiveSignatures)
            {
                if (StartsWith(bytes, signature))
                {
                    return Rejected("archive");
                }
            }

            var text = DecodeText(bytes);
            var trimmed = text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            if (trimmed.Length == 0)
            {
                return Rejected("empty");
            }

            var prefixLength = Math.Min(trimmed.Length, 4096);
            var lower = trimmed.Substring(0, prefixLength).ToLowerInvariant();
            if (lower.StartsWith("<!doctype html", StringComparison.Ordinal) ||
                lower.StartsWith("<html", StringComparison.Ordinal) ||
                lower.StartsWith("<head", StringComparison.Ordinal) ||
                lower.StartsWith("<body", StringComparison.Ordinal) ||
                lower.StartsWith("<?xml", StringComparison.Ordinal) ||
                lower.StartsWith("{\"code\"", StringComparison.Ordinal) ||
                lower.StartsWith("{\"error\"", StringComparison.Ordinal))
            {
                return Rejected("html-or-json");
            }

            var replacementCharacters = CountCharacter(text, '\uFFFD');
            if (replacementCharacters > 0)
            {
                return new SubtitleContentInspection(
                    false,
                    "replacement-characters",
                    replacementCharacters,
                    0);
            }

            var mojibakeMarkers = 0;
            foreach (var marker in MojibakeMarkers)
            {
                mojibakeMarkers += CountOccurrences(text, marker);
            }

            if (mojibakeMarkers > 0)
            {
                return new SubtitleContentInspection(
                    false,
                    "mojibake-markers",
                    0,
                    mojibakeMarkers);
            }

            if (!HasAcceptableControlCharacterRatio(text))
            {
                return new SubtitleContentInspection(
                    false,
                    "control-characters",
                    0,
                    0);
            }

            return new SubtitleContentInspection(true, null, 0, 0);
        }

        private static string DecodeText(byte[] bytes)
        {
            if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe)
            {
                var count = bytes.Length - ((bytes.Length - 2) % 2);
                return Encoding.Unicode.GetString(bytes, 2, count - 2);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff)
            {
                var count = bytes.Length - ((bytes.Length - 2) % 2);
                return Encoding.BigEndianUnicode.GetString(bytes, 2, count - 2);
            }

            return Encoding.UTF8.GetString(bytes);
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

        private static int CountCharacter(string value, char character)
        {
            var count = 0;
            foreach (var item in value)
            {
                if (item == character)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountOccurrences(string value, string marker)
        {
            var count = 0;
            var startIndex = 0;
            while ((startIndex = value.IndexOf(marker, startIndex, StringComparison.Ordinal)) >= 0)
            {
                count++;
                startIndex += marker.Length;
            }

            return count;
        }

        private static SubtitleContentInspection Rejected(string reason) =>
            new SubtitleContentInspection(false, reason, 0, 0);

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
