namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Renders administrative email (docs/TRELLISKIT.md section 12.2): picks the subject, preheader, heading, and body
    /// (property override, else template default), lets the template compose its blocks (<see cref="EmailTemplate.Compose"/>),
    /// and hands the document to the template's layout (<see cref="StandardEmailLayout"/> by default), which produces a
    /// complete, inline-styled HTML document in the property's brand plus a matching plain-text version. Every token
    /// value (member-supplied names, reasons, excerpts) is encoded, never raw HTML.
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class EmailRenderer
    {
        #region Public-Members

        /// <summary>
        /// Longest override subject, preheader, or heading.
        /// </summary>
        public const int MaxLineLength = 200;

        /// <summary>
        /// Longest override body.
        /// </summary>
        public const int MaxBodyLength = 5000;

        #endregion

        #region Private-Members

        private static readonly Regex _Token = new Regex("\\{([A-Za-z][A-Za-z0-9]*)\\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Render a template with its override and token values.
        /// </summary>
        /// <param name="template">Template.</param>
        /// <param name="overrides">Property override, or null.</param>
        /// <param name="tokens">Token values (missing tokens render empty).</param>
        /// <param name="brand">Brand.</param>
        /// <returns>Subject, preheader, HTML, and text.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static EmailRendered Render(EmailTemplate template, EmailTemplateOverride? overrides, IReadOnlyDictionary<string, string> tokens, EmailBrand brand)
        {
            ArgumentNullException.ThrowIfNull(template);
            ArgumentNullException.ThrowIfNull(tokens);
            ArgumentNullException.ThrowIfNull(brand);
            EmailComposer composer = new EmailComposer(template, tokens, brand,
                Fill(Pick(overrides?.Subject, template.DefaultSubject), tokens),
                Fill(Pick(overrides?.Preheader, template.DefaultPreheader), tokens),
                Fill(Pick(overrides?.Heading, template.DefaultHeading), tokens),
                Pick(overrides?.Body, template.DefaultBody));
            template.Compose(composer);
            EmailLayout layout = template.Layout ?? StandardEmailLayout.Instance;
            return layout.Render(composer.ToDocument(), brand);
        }

        /// <summary>
        /// Validate an override against a template's tokens and limits.
        /// </summary>
        /// <param name="template">Template.</param>
        /// <param name="value">Override.</param>
        /// <returns>Field errors (empty when valid).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static Dictionary<string, string> Validate(EmailTemplate template, EmailTemplateOverride value)
        {
            ArgumentNullException.ThrowIfNull(template);
            ArgumentNullException.ThrowIfNull(value);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            HashSet<string> allowed = new HashSet<string>(template.Tokens, StringComparer.Ordinal);
            void Check(string field, string? text, int max)
            {
                if (text == null) return;
                if (text.Length > max) { errors[field] = "At most " + max + " characters."; return; }
                List<string> unknown = _Token.Matches(text).Select(m => m.Groups[1].Value).Where(t => !allowed.Contains(t)).Distinct(StringComparer.Ordinal).ToList();
                if (unknown.Count > 0) errors[field] = "Unknown token " + String.Join(", ", unknown.Select(u => "{" + u + "}")) + "; use " + String.Join(", ", template.Tokens.Select(t => "{" + t + "}")) + ".";
                if (field != "body" && (text.Contains('\n', StringComparison.Ordinal) || text.Contains('\r', StringComparison.Ordinal))) errors[field] = "Use a single line.";
            }
            Check("subject", value.Subject, MaxLineLength);
            Check("preheader", value.Preheader, MaxLineLength);
            Check("heading", value.Heading, MaxLineLength);
            Check("body", value.Body, MaxBodyLength);
            if (value.Subject != null && value.Subject.Trim().Length == 0) errors["subject"] = "Use at least one character, or null for the default.";
            return errors;
        }

        /// <summary>
        /// The {token} placeholders in text that a template does not accept.
        /// </summary>
        /// <param name="template">Template.</param>
        /// <param name="text">Text (null has none).</param>
        /// <returns>Unknown token names, distinct, in order of appearance.</returns>
        /// <exception cref="ArgumentNullException">template is null.</exception>
        public static List<string> UnknownTokens(EmailTemplate template, string? text)
        {
            ArgumentNullException.ThrowIfNull(template);
            if (String.IsNullOrEmpty(text)) return new List<string>();
            HashSet<string> allowed = new HashSet<string>(template.Tokens, StringComparer.Ordinal);
            return _Token.Matches(text).Select(m => m.Groups[1].Value).Where(t => !allowed.Contains(t)).Distinct(StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Replace {token} placeholders with values (unknown tokens render empty).
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="tokens">Values.</param>
        /// <returns>Filled text.</returns>
        public static string Fill(string text, IReadOnlyDictionary<string, string> tokens)
        {
            if (String.IsNullOrEmpty(text)) return String.Empty;
            return _Token.Replace(text, m => tokens.TryGetValue(m.Groups[1].Value, out string? value) ? value ?? String.Empty : String.Empty);
        }

        #endregion

        #region Private-Methods

        private static string Pick(string? value, string fallback)
        {
            return String.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        #endregion
    }
}
