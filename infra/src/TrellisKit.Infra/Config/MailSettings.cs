namespace TrellisKit.Infra.Config
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Deployment-level (platform) mail of the backend: the provider and the Mailgun sending domain and API region
    /// (settings email.provider, email.mailgunDomain, email.mailgunBaseUrl, all live), and whether the Amazon SES
    /// identity (the Email stack) exists. Read from the mail section of a property's deploy/infra.json (per-property
    /// topology) or of the deployment file (shared topology). Every value is optional; an unset value is not passed to
    /// the server, so the backend default and the admin console apply. The Mailgun API key never appears here: it comes
    /// only from the Secrets Manager secret {prefix}/mailgun.
    /// Provider values: the backend's own (Auto, Mailgun, Smtp, Log), passed through as TK_EMAIL_PROVIDER, plus Ses
    /// (send through the SES SMTP endpoint, TK_EMAIL_PROVIDER=Smtp) and None (no mail infrastructure and no
    /// TK_EMAIL_PROVIDER, so the backend default and the console apply). Mailgun and None create no SES identity and no
    /// SES DNS records and pass no SES SMTP endpoint; every other value, and an unset provider (the engine default,
    /// matching the backend's Auto with the SES SMTP host set), keeps the SES identity.
    /// Thread safety: not thread safe; configure before building stacks.
    /// </summary>
    public class MailSettings
    {
        /// <summary>
        /// Providers the backend accepts for TK_EMAIL_PROVIDER (EmailProviderEnum).
        /// </summary>
        public static readonly IReadOnlyList<string> BackendProviders = new List<string> { "Auto", "Mailgun", "Smtp", "Log" };

        /// <summary>
        /// Every accepted mail.provider value: the backend providers plus Ses and None.
        /// </summary>
        public static readonly IReadOnlyList<string> Providers = new List<string> { "Auto", "Mailgun", "Smtp", "Log", "Ses", "None" };

        private static readonly Regex _HostPattern = new Regex("^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$");

        private string _Provider = String.Empty;
        private string _MailgunDomain = String.Empty;
        private string _MailgunBaseUrl = String.Empty;

        /// <summary>
        /// Deployment mail provider: Auto, Mailgun, Smtp, Log, Ses, or None, or empty to leave the live setting unset and
        /// keep the SES identity (the engine default). See <see cref="ServerProvider"/> and
        /// <see cref="CreatesSesIdentity"/>. Default empty.
        /// </summary>
        /// <exception cref="ArgumentException">The value is not a known provider.</exception>
        public string Provider
        {
            get
            {
                return _Provider;
            }
            set
            {
                string text = (value ?? String.Empty).Trim();
                if (text.Length > 0 && !Providers.Contains(text))
                {
                    throw new ArgumentException("mail.provider must be one of " + String.Join(", ", Providers) + ", got '" + text + "'.", nameof(Provider));
                }

                _Provider = text;
            }
        }

        /// <summary>
        /// Mailgun sending domain for deployment mail (TK_MAILGUN_DOMAIN), for example mg.example.test, or empty.
        /// Default empty.
        /// </summary>
        /// <exception cref="ArgumentException">The value is not a host name.</exception>
        public string MailgunDomain
        {
            get
            {
                return _MailgunDomain;
            }
            set
            {
                string text = (value ?? String.Empty).Trim().ToLowerInvariant();
                if (text.Length > 0 && !_HostPattern.IsMatch(text))
                {
                    throw new ArgumentException("mail.mailgunDomain must be a domain name, got '" + text + "'.", nameof(MailgunDomain));
                }

                _MailgunDomain = text;
            }
        }

        /// <summary>
        /// Mailgun API base URL (TK_MAILGUN_BASE_URL), https://api.mailgun.net/v3/ (US) or https://api.eu.mailgun.net/v3/
        /// (EU), or empty for the backend default (US). Default empty.
        /// </summary>
        /// <exception cref="ArgumentException">The value is not an absolute https URL.</exception>
        public string MailgunBaseUrl
        {
            get
            {
                return _MailgunBaseUrl;
            }
            set
            {
                string text = (value ?? String.Empty).Trim();
                if (text.Length > 0
                    && (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps || String.IsNullOrEmpty(uri.Host)))
                {
                    throw new ArgumentException("mail.mailgunBaseUrl must be an absolute https URL, got '" + text + "'.", nameof(MailgunBaseUrl));
                }

                _MailgunBaseUrl = text;
            }
        }

        /// <summary>
        /// Whether the deployment sends through Mailgun: a Mailgun domain is set or the provider is Mailgun. Only then
        /// is the Mailgun secret created and TK_MAILGUN_API_KEY passed to the server.
        /// </summary>
        public bool UsesMailgun
        {
            get { return _MailgunDomain.Length > 0 || _Provider == "Mailgun"; }
        }

        /// <summary>
        /// Whether the Amazon SES identity (Email stack: domain identity, DKIM, MAIL FROM MX and SPF, DMARC, configuration
        /// set) is created and the server receives the SES SMTP endpoint (TK_SMTP_HOST, TK_SMTP_PORT, TK_SMTP_USE_TLS):
        /// false only when the provider is Mailgun or None.
        /// </summary>
        public bool CreatesSesIdentity
        {
            get { return _Provider != "Mailgun" && _Provider != "None"; }
        }

        /// <summary>
        /// Value passed as TK_EMAIL_PROVIDER, or empty to pass none: Ses becomes Smtp (SES is reached over SMTP), None
        /// and an unset provider pass nothing, and the backend providers pass through unchanged.
        /// </summary>
        public string ServerProvider
        {
            get
            {
                if (_Provider == "Ses") return "Smtp";
                if (_Provider == "None") return String.Empty;
                return _Provider;
            }
        }

        /// <summary>
        /// Read the mail section of a deploy/infra.json or deployment file. A missing or non-object section yields empty
        /// settings.
        /// </summary>
        /// <param name="root">Root element of the file.</param>
        /// <param name="file">File path, used in error messages.</param>
        /// <returns>Settings.</returns>
        /// <exception cref="ArgumentException">A value is invalid.</exception>
        public static MailSettings FromJson(JsonElement root, string file)
        {
            MailSettings settings = new MailSettings();
            if (root.ValueKind != JsonValueKind.Object) return settings;
            if (!root.TryGetProperty("mail", out JsonElement mail) || mail.ValueKind != JsonValueKind.Object) return settings;

            try
            {
                settings.Provider = Text(mail, "provider");
                settings.MailgunDomain = Text(mail, "mailgunDomain");
                settings.MailgunBaseUrl = Text(mail, "mailgunBaseUrl");
            }
            catch (ArgumentException e)
            {
                throw new ArgumentException("Invalid mail section in " + file + ": " + e.Message, e);
            }

            return settings;
        }

        private static string Text(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String) return String.Empty;
            return value.GetString() ?? String.Empty;
        }
    }
}
