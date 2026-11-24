namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Parses link preview metadata from HTML (contract section 5.9): Open Graph first, then Twitter card tags, then
    /// the title element and meta name=description. Text is entity-decoded, stripped of markup, normalized like a
    /// single-line body field, and cut to <see cref="MaxTitleLength"/> and <see cref="MaxDescriptionLength"/> Unicode
    /// scalar values (an ellipsis character marks a cut). Never executes or renders the page.
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class LinkPreviewHtmlParser
    {
        #region Public-Members

        /// <summary>
        /// Longest title.
        /// </summary>
        public const int MaxTitleLength = 200;

        /// <summary>
        /// Longest description.
        /// </summary>
        public const int MaxDescriptionLength = 300;

        /// <summary>
        /// Longest site name.
        /// </summary>
        public const int MaxSiteNameLength = 100;

        #endregion

        #region Private-Members

        private static readonly TimeSpan _Timeout = TimeSpan.FromSeconds(2);
        private static readonly Regex _Meta = new Regex("<meta\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, _Timeout);
        private static readonly Regex _Link = new Regex("<link\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, _Timeout);
        private static readonly Regex _Attribute = new Regex("([a-zA-Z_:][-a-zA-Z0-9_:.]*)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s\"'>]+))", RegexOptions.CultureInvariant, _Timeout);
        private static readonly Regex _Title = new Regex("<title\\b[^>]*>(.*?)</title\\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, _Timeout);
        private static readonly Regex _Tag = new Regex("<[^>]*>", RegexOptions.CultureInvariant, _Timeout);
        private static readonly Regex _Comment = new Regex("<!--.*?-->", RegexOptions.Singleline | RegexOptions.CultureInvariant, _Timeout);
        private static readonly Regex _Charset = new Regex("<meta\\b[^>]*charset\\s*=\\s*[\"']?([-a-zA-Z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, _Timeout);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Decode page bytes: the Content-Type charset, else a meta charset, else UTF-8 (unknown charsets fall back to UTF-8).
        /// </summary>
        /// <param name="body">Bytes.</param>
        /// <param name="contentType">Content-Type header, or null.</param>
        /// <returns>Text.</returns>
        public static string Decode(byte[]? body, string? contentType)
        {
            if (body == null || body.Length == 0) return String.Empty;
            string? charset = null;
            if (!String.IsNullOrEmpty(contentType))
            {
                int index = contentType.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
                if (index >= 0) charset = contentType.Substring(index + 8).Trim().Trim('"', '\'', ';', ' ');
            }
            if (charset == null)
            {
                string probe = Encoding.ASCII.GetString(body, 0, Math.Min(body.Length, 4096));
                Match match = SafeMatch(_Charset, probe);
                if (match.Success) charset = match.Groups[1].Value;
            }
            Encoding encoding = Encoding.UTF8;
            if (!String.IsNullOrEmpty(charset))
            {
                try
                {
                    encoding = Encoding.GetEncoding(charset);
                }
                catch (ArgumentException)
                {
                    encoding = Encoding.UTF8;
                }
            }
            return encoding.GetString(body);
        }

        /// <summary>
        /// Parse metadata from page HTML.
        /// </summary>
        /// <param name="html">HTML text, or null.</param>
        /// <returns>Metadata (fields null when absent or empty).</returns>
        public static LinkPreviewMetadata Parse(string? html)
        {
            LinkPreviewMetadata result = new LinkPreviewMetadata();
            if (String.IsNullOrEmpty(html)) return result;
            string text = SafeReplace(_Comment, html, String.Empty);
            Dictionary<string, string> meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match tag in SafeMatches(_Meta, text))
            {
                Dictionary<string, string> attributes = Attributes(tag.Value);
                string? key = Get(attributes, "property") ?? Get(attributes, "name") ?? Get(attributes, "itemprop");
                string? content = Get(attributes, "content");
                if (key == null || content == null) continue;
                key = key.Trim();
                if (!meta.ContainsKey(key)) meta[key] = content;
            }
            string? canonicalLink = null;
            foreach (Match tag in SafeMatches(_Link, text))
            {
                Dictionary<string, string> attributes = Attributes(tag.Value);
                string? rel = Get(attributes, "rel");
                if (rel != null && rel.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("canonical", StringComparer.OrdinalIgnoreCase))
                {
                    canonicalLink = Get(attributes, "href");
                    if (canonicalLink != null) break;
                }
            }
            string? titleElement = null;
            Match title = SafeMatch(_Title, text);
            if (title.Success) titleElement = title.Groups[1].Value;

            result.Title = Clean(First(meta, "og:title", "twitter:title") ?? titleElement, MaxTitleLength);
            result.Description = Clean(First(meta, "og:description", "twitter:description", "description"), MaxDescriptionLength);
            result.SiteName = Clean(First(meta, "og:site_name"), MaxSiteNameLength);
            result.ImageUrl = Url(First(meta, "og:image", "og:image:url", "og:image:secure_url", "twitter:image", "twitter:image:src"));
            result.CanonicalUrl = Url(First(meta, "og:url") ?? canonicalLink);
            return result;
        }

        /// <summary>
        /// Decode entities, strip markup, normalize to a single line, and cut to a length.
        /// </summary>
        /// <param name="value">Raw text, or null.</param>
        /// <param name="maxLength">Longest result in Unicode scalar values (an ellipsis replaces the last one when cut).</param>
        /// <returns>Clean text, or null when empty.</returns>
        public static string? Clean(string? value, int maxLength)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string text = SafeReplace(_Tag, value, " ");
            text = WebUtility.HtmlDecode(text);
            text = SafeReplace(_Tag, text, " ");
            text = MarkdownNormalizer.NormalizeSingleLine(text);
            if (text.Length == 0) return null;
            return Truncate(text, Math.Max(2, maxLength));
        }

        #endregion

        #region Private-Methods

        private static string Truncate(string text, int maxLength)
        {
            if (MarkdownNormalizer.Length(text) <= maxLength) return text;
            StringBuilder sb = new StringBuilder();
            int count = 0;
            for (int i = 0; i < text.Length && count < maxLength - 1; i++)
            {
                sb.Append(text[i]);
                if (Char.IsHighSurrogate(text[i]) && i + 1 < text.Length) sb.Append(text[++i]);
                count++;
            }
            return sb.ToString().TrimEnd() + "\u2026";
        }

        private static string? Url(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string decoded = WebUtility.HtmlDecode(value).Trim();
            return decoded.Length == 0 || decoded.Length > 2048 ? null : decoded;
        }

        private static string? First(Dictionary<string, string> meta, params string[] keys)
        {
            foreach (string key in keys)
            {
                if (meta.TryGetValue(key, out string? value) && !String.IsNullOrWhiteSpace(value)) return value;
            }
            return null;
        }

        private static Dictionary<string, string> Attributes(string tag)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in SafeMatches(_Attribute, tag))
            {
                string name = match.Groups[1].Value;
                string value = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Success ? match.Groups[3].Value : match.Groups[4].Value;
                if (!result.ContainsKey(name)) result[name] = value;
            }
            return result;
        }

        private static string? Get(Dictionary<string, string> attributes, string name)
        {
            return attributes.TryGetValue(name, out string? value) ? value : null;
        }

        private static Match SafeMatch(Regex regex, string input)
        {
            try
            {
                return regex.Match(input);
            }
            catch (RegexMatchTimeoutException)
            {
                return Match.Empty;
            }
        }

        private static List<Match> SafeMatches(Regex regex, string input)
        {
            List<Match> result = new List<Match>();
            try
            {
                foreach (Match match in regex.Matches(input)) result.Add(match);
            }
            catch (RegexMatchTimeoutException)
            {
                // A pathological page yields what was matched so far.
            }
            return result;
        }

        private static string SafeReplace(Regex regex, string input, string replacement)
        {
            try
            {
                return regex.Replace(input, replacement);
            }
            catch (RegexMatchTimeoutException)
            {
                return input;
            }
        }

        #endregion
    }
}
