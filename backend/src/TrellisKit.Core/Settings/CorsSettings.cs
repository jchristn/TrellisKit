namespace TrellisKit.Core.Settings
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Cross-origin resource sharing settings. Applied live.
    /// </summary>
    public class CorsSettings
    {
        #region Public-Members

        /// <summary>
        /// Allowed browser origins, for example "https://example.test". A single "*" allows any origin.
        /// Default: local development origins. Never null.
        /// </summary>
        public List<string> AllowedOrigins
        {
            get
            {
                return _AllowedOrigins;
            }
            set
            {
                _AllowedOrigins = (value ?? new List<string>())
                    .Where(origin => !String.IsNullOrWhiteSpace(origin))
                    .Select(origin => origin.Trim().TrimEnd('/'))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        #endregion

        #region Private-Members

        private List<string> _AllowedOrigins = new List<string>
        {
            "http://127.0.0.1:5173",
            "http://localhost:5173",
            "http://127.0.0.1:8080",
            "http://localhost:8080"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve the value for the Access-Control-Allow-Origin header for a request origin.
        /// </summary>
        /// <param name="origin">The request's Origin header, or null.</param>
        /// <returns>The origin to echo, "*" when any origin is allowed, or null when the origin is not allowed.</returns>
        public string? ResolveAllowedOrigin(string? origin)
        {
            if (_AllowedOrigins.Any(o => o == "*")) return String.IsNullOrEmpty(origin) ? "*" : origin;
            if (String.IsNullOrWhiteSpace(origin)) return null;
            string normalized = origin.Trim().TrimEnd('/');
            return _AllowedOrigins.Any(o => String.Equals(o, normalized, StringComparison.OrdinalIgnoreCase)) ? normalized : null;
        }

        #endregion
    }
}
