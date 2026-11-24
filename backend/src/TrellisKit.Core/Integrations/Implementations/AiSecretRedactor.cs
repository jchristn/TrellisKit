namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Removes an endpoint's secrets from text before it is logged, stored, or returned (docs/AI_CONTRACT.md section
    /// 2.4): every decrypted secret (exact match), the value of the configured query parameter in URLs, and the values
    /// of the configured auth header and extra headers in "Name: value" text.
    /// Thread safety: immutable; safe to call concurrently.
    /// </summary>
    public sealed class AiSecretRedactor
    {
        #region Public-Members

        /// <summary>
        /// Replacement text.
        /// </summary>
        public const string Redacted = "[redacted]";

        /// <summary>
        /// A redactor that changes nothing.
        /// </summary>
        public static readonly AiSecretRedactor None = new AiSecretRedactor(new EndpointCredentials());

        #endregion

        #region Private-Members

        private readonly List<string> _Secrets;
        private readonly Regex? _Query;
        private readonly List<Regex> _Headers = new List<Regex>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a redactor for one endpoint's credentials.
        /// </summary>
        /// <param name="credentials">Decrypted credentials.</param>
        /// <exception cref="ArgumentNullException">The credentials are null.</exception>
        public AiSecretRedactor(EndpointCredentials credentials)
        {
            ArgumentNullException.ThrowIfNull(credentials);
            _Secrets = credentials.SecretValues.Where(s => s.Length >= 1).Distinct(StringComparer.Ordinal).OrderByDescending(s => s.Length).ToList();
            if (!String.IsNullOrEmpty(credentials.QueryParameter))
                _Query = new Regex("([?&]" + Regex.Escape(Uri.EscapeDataString(credentials.QueryParameter)) + "=)[^&#\\s\"']*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            List<string> names = credentials.ExtraHeaders.Select(h => h.Key).ToList();
            if (!String.IsNullOrEmpty(credentials.HeaderName)) names.Add(credentials.HeaderName);
            foreach (string name in names.Where(n => !String.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase))
                _Headers.Add(new Regex("(\"?" + Regex.Escape(name) + "\"?\\s*[:=]\\s*\"?)[^\"\\r\\n,}]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Redact text.
        /// </summary>
        /// <param name="text">Text, or null.</param>
        /// <returns>Redacted text, or null.</returns>
        public string? Redact(string? text)
        {
            if (String.IsNullOrEmpty(text)) return text;
            string result = text;
            foreach (string secret in _Secrets) result = result.Replace(secret, Redacted, StringComparison.Ordinal);
            if (_Query != null) result = _Query.Replace(result, "$1" + Redacted);
            foreach (Regex header in _Headers) result = header.Replace(result, "$1" + Redacted);
            return result;
        }

        #endregion
    }
}
