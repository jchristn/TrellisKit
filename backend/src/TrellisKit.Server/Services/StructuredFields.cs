namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Minimal RFC 8941 structured field support for the Expo Updates protocol headers: dictionaries
    /// (expo-expect-signature, expo-extra-params, expo-signature) and lists of strings (expo-recent-failed-update-ids).
    /// Parameters are ignored; bare keys read as "true". Malformed input yields what parsed before the error.
    /// Thread safety: stateless.
    /// </summary>
    public static class StructuredFields
    {
        #region Public-Methods

        /// <summary>
        /// Parse a dictionary, for example sig, keyid="main", alg="rsa-v1_5-sha256".
        /// </summary>
        /// <param name="header">Header value, or null.</param>
        /// <returns>Keys (lowercase) to string values. Never null.</returns>
        public static Dictionary<string, string> ParseDictionary(string? header)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (String.IsNullOrWhiteSpace(header)) return result;
            int i = 0;
            string text = header;
            while (i < text.Length)
            {
                SkipSpace(text, ref i);
                int start = i;
                while (i < text.Length && (Char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '-' || text[i] == '.' || text[i] == '*')) i++;
                if (i == start) break;
                string key = text.Substring(start, i - start).ToLowerInvariant();
                string value = "true";
                if (i < text.Length && text[i] == '=')
                {
                    i++;
                    string? item = ReadItem(text, ref i);
                    if (item == null) break;
                    value = item;
                }
                SkipParameters(text, ref i);
                result[key] = value;
                SkipSpace(text, ref i);
                if (i < text.Length && text[i] == ',') i++;
                else break;
            }
            return result;
        }

        /// <summary>
        /// Parse a list of items (strings or tokens), for example "uuid-1", "uuid-2".
        /// </summary>
        /// <param name="header">Header value, or null.</param>
        /// <returns>Items in order. Never null.</returns>
        public static List<string> ParseList(string? header)
        {
            List<string> result = new List<string>();
            if (String.IsNullOrWhiteSpace(header)) return result;
            int i = 0;
            string text = header;
            while (i < text.Length)
            {
                SkipSpace(text, ref i);
                string? item = ReadItem(text, ref i);
                if (item == null) break;
                result.Add(item);
                SkipParameters(text, ref i);
                SkipSpace(text, ref i);
                if (i < text.Length && text[i] == ',') i++;
                else break;
            }
            return result;
        }

        /// <summary>
        /// Serialize a dictionary of string values (each written as a quoted string).
        /// </summary>
        /// <param name="entries">Keys and values in order.</param>
        /// <returns>Header value.</returns>
        /// <exception cref="ArgumentNullException">The entries are null.</exception>
        public static string SerializeDictionary(IEnumerable<KeyValuePair<string, string>> entries)
        {
            ArgumentNullException.ThrowIfNull(entries);
            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, string> entry in entries) parts.Add(entry.Key + "=" + Quote(entry.Value));
            return String.Join(", ", parts);
        }

        /// <summary>
        /// Quote a string item.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Quoted string with backslash and quote escaped.</returns>
        public static string Quote(string? value)
        {
            return "\"" + (value ?? String.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        #endregion

        #region Private-Methods

        private static string? ReadItem(string text, ref int i)
        {
            if (i >= text.Length) return null;
            if (text[i] == '"')
            {
                StringBuilder builder = new StringBuilder();
                i++;
                while (i < text.Length)
                {
                    char c = text[i];
                    if (c == '\\' && i + 1 < text.Length)
                    {
                        builder.Append(text[i + 1]);
                        i += 2;
                        continue;
                    }
                    if (c == '"')
                    {
                        i++;
                        return builder.ToString();
                    }
                    builder.Append(c);
                    i++;
                }
                return null;
            }
            if (text[i] == '?' && i + 1 < text.Length)
            {
                string boolean = text[i + 1] == '1' ? "true" : "false";
                i += 2;
                return boolean;
            }
            int start = i;
            while (i < text.Length && text[i] != ',' && text[i] != ';' && !Char.IsWhiteSpace(text[i])) i++;
            return i == start ? null : text.Substring(start, i - start);
        }

        private static void SkipParameters(string text, ref int i)
        {
            while (i < text.Length && text[i] == ';')
            {
                i++;
                while (i < text.Length && text[i] != ',' && text[i] != ';')
                {
                    if (text[i] == '"')
                    {
                        ReadItem(text, ref i);
                        continue;
                    }
                    i++;
                }
            }
        }

        private static void SkipSpace(string text, ref int i)
        {
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
        }

        #endregion
    }
}
