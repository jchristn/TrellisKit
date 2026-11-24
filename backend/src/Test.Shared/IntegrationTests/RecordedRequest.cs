namespace Test.Shared.IntegrationTests
{
    using System;
    using System.Collections.Generic;
    using System.Net;

    /// <summary>
    /// A request captured by <see cref="FakeHttpHandler"/>.
    /// </summary>
    public sealed class RecordedRequest
    {
        #region Public-Members

        /// <summary>
        /// HTTP method.
        /// </summary>
        public string Method { get; set; } = String.Empty;

        /// <summary>
        /// Absolute URL.
        /// </summary>
        public Uri Url { get; set; } = new Uri("http://127.0.0.1/");

        /// <summary>
        /// Request and content headers (values joined with commas), case-insensitive.
        /// </summary>
        public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Body text, or empty.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read a header.
        /// </summary>
        /// <param name="name">Header name.</param>
        /// <returns>Value, or null.</returns>
        public string? Header(string name)
        {
            return Headers.TryGetValue(name, out string? value) ? value : null;
        }

        /// <summary>
        /// Parse the query string.
        /// </summary>
        /// <returns>Decoded values by name (last wins).</returns>
        public Dictionary<string, string> Query()
        {
            return ParsePairs(Url.Query.TrimStart('?'));
        }

        /// <summary>
        /// Parse a form-encoded body.
        /// </summary>
        /// <returns>Decoded values by name (last wins).</returns>
        public Dictionary<string, string> Form()
        {
            return ParsePairs(Body);
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, string> ParsePairs(string text)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (String.IsNullOrEmpty(text)) return result;
            foreach (string pair in text.Split('&'))
            {
                if (pair.Length == 0) continue;
                int equals = pair.IndexOf('=');
                string key = equals < 0 ? pair : pair.Substring(0, equals);
                string value = equals < 0 ? String.Empty : pair.Substring(equals + 1);
                result[WebUtility.UrlDecode(key)] = WebUtility.UrlDecode(value);
            }
            return result;
        }

        #endregion
    }
}
