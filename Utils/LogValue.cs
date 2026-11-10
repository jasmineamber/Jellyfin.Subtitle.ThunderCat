using System;
using System.Text;

namespace Jellyfin.Plugin.ThunderCat.Utils
{
    internal static class LogValue
    {
        public static string Sanitize(string? value, int maxLength = 300)
        {
            if (string.IsNullOrWhiteSpace(value) || maxLength <= 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(Math.Min(value.Length, maxLength));
            foreach (var character in value)
            {
                if (builder.Length >= maxLength)
                {
                    break;
                }

                switch (character)
                {
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\t':
                        builder.Append(' ');
                        break;
                    default:
                        builder.Append(character);
                        break;
                }
            }

            return builder.ToString().Trim();
        }
    }
}
