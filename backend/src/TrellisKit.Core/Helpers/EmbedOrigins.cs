namespace TrellisKit.Core.Helpers
{
    using System;
    using System.Globalization;
    using System.Text.RegularExpressions;

    /// <summary>
    /// The rule for assistant embed origins (docs/AI_CONTRACT.md section 13.3): https://host[:port] by default; plain
    /// http only for the local development hosts localhost and 127.0.0.1, on any port. shared/src/embedOrigins.js
    /// mirrors this class; change both together.
    /// Thread safety: stateless and safe to call concurrently.
    /// </summary>
    public static class EmbedOrigins
    {
        #region Private-Members

        private static readonly Regex _Origin = new Regex("^(https?)://([a-z0-9.-]+)(:([0-9]{1,5}))?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Normalize an origin: trimmed, lowercase, without a trailing slash.
        /// </summary>
        /// <param name="value">Origin as entered.</param>
        /// <returns>Normalized origin, or null when it is not an allowed embed origin.</returns>
        public static string? Normalize(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string origin = value.Trim().TrimEnd('/').ToLowerInvariant();
            Match match = _Origin.Match(origin);
            if (!match.Success) return null;
            string host = match.Groups[2].Value;
            if (host.StartsWith(".", StringComparison.Ordinal) || host.EndsWith(".", StringComparison.Ordinal) || host.StartsWith("-", StringComparison.Ordinal) || host.Contains("..", StringComparison.Ordinal)) return null;
            if (match.Groups[4].Success)
            {
                int port = Int32.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
                if (port < 1 || port > 65535) return null;
            }
            if (match.Groups[1].Value == "http" && !IsLocalHost(host)) return null;
            return origin;
        }

        /// <summary>
        /// Whether an origin is a local development origin (localhost or 127.0.0.1, http or https, any port).
        /// </summary>
        /// <param name="origin">Origin.</param>
        /// <returns>True for a local development origin.</returns>
        public static bool IsLocalDevelopment(string? origin)
        {
            if (String.IsNullOrWhiteSpace(origin)) return false;
            Match match = _Origin.Match(origin.Trim().TrimEnd('/').ToLowerInvariant());
            return match.Success && IsLocalHost(match.Groups[2].Value);
        }

        /// <summary>
        /// The validation message for a rejected origin.
        /// </summary>
        /// <param name="value">Origin as entered.</param>
        /// <returns>Message naming the origin.</returns>
        public static string Problem(string? value)
        {
            return "'" + (value ?? String.Empty).Trim() + "' is not allowed: use https://host[:port]; plain http is allowed only for http://localhost[:port] and http://127.0.0.1[:port] (local development).";
        }

        #endregion

        #region Private-Methods

        private static bool IsLocalHost(string host)
        {
            return host == "localhost" || host == "127.0.0.1";
        }

        #endregion
    }
}
