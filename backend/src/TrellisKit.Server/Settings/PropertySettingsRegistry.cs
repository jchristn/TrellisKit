namespace TrellisKit.Server.Settings
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The per-property settings surface (docs/MULTIPROPERTY_CONTRACT.md section 5.2), in display order. Today it holds the
    /// administrative email integration (docs/TRELLISKIT.md section 12.1): per-property Mailgun credentials are set only
    /// here (encrypted with the engine secrets key), never from per-property environment variables.
    /// </summary>
    public static class PropertySettingsRegistry
    {
        #region Public-Members

        /// <summary>
        /// Every per-property setting.
        /// </summary>
        public static readonly IReadOnlyList<PropertySettingDefinition> All = Build();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Find a definition by key (case-insensitive).
        /// </summary>
        /// <param name="key">Key.</param>
        /// <returns>Definition, or null.</returns>
        public static PropertySettingDefinition? Find(string? key)
        {
            if (String.IsNullOrWhiteSpace(key)) return null;
            return All.FirstOrDefault(d => String.Equals(d.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        #endregion

        #region Private-Methods

        private static List<PropertySettingDefinition> Build()
        {
            const string email = "Email";
            List<PropertySettingDefinition> all = new List<PropertySettingDefinition>
            {
                new PropertySettingDefinition("email.provider", email, "Provider",
                    "Auto (default) uses Mailgun when its domain and API key are set, else this property's SMTP host (in SingleProperty also the deployment's SMTP settings), else the log.",
                    SettingTypeEnum.Enum, "Auto", Enum.GetNames<EmailProviderEnum>().ToList()),
                new PropertySettingDefinition("email.fromName", email, "From name", "Sender name. Empty uses property.json contact.emailFromName.", SettingTypeEnum.String, maxLength: 100),
                new PropertySettingDefinition("email.fromAddress", email, "From address", "Sender address. Empty uses property.json contact.emailFromAddress.", SettingTypeEnum.String, maxLength: 254),
                new PropertySettingDefinition("email.replyTo", email, "Reply-to address", "Reply-to address (empty uses the support email).", SettingTypeEnum.String, maxLength: 254),
                new PropertySettingDefinition("email.trackOpens", email, "Track opens",
                    "Let Mailgun add an open-tracking pixel to optional email (digests, new post emails, announcements). Essential and staff email is never tracked.", SettingTypeEnum.Boolean, "false"),
                new PropertySettingDefinition("email.trackClicks", email, "Track clicks",
                    "Let Mailgun rewrite links in optional email to count clicks. Essential and staff email (sign-in, reset, and billing links) is never rewritten.", SettingTypeEnum.Boolean, "false"),
                new PropertySettingDefinition("email.postalAddress", email, "Postal address", "Mailing address shown in the email footer. Empty uses email/layout.json postalAddress.", SettingTypeEnum.String, maxLength: 300),
                new PropertySettingDefinition("email.mailgunDomain", email, "Mailgun sending domain", "For example mg.example.test.", SettingTypeEnum.String, maxLength: 253),
                new PropertySettingDefinition("email.mailgunApiKey", email, "Mailgun API key", "Sending API key (stored encrypted).", SettingTypeEnum.Secret),
                new PropertySettingDefinition("email.mailgunRegion", email, "Mailgun region", "Us (api.mailgun.net) or Eu (api.eu.mailgun.net).", SettingTypeEnum.Enum, "Us", new List<string> { "Us", "Eu" }),
                new PropertySettingDefinition("email.mailgunWebhookSigningKey", email, "Mailgun webhook signing key",
                    "Verifies POST /api/v1/webhooks/mailgun/{propertyId} (delivered, failed, complained, unsubscribed). Stored encrypted.", SettingTypeEnum.Secret),
                new PropertySettingDefinition("email.validationEnabled", email, "Validate addresses at sign-up",
                    "Check new addresses with Mailgun validation: high risk or undeliverable addresses are refused; unknown results are allowed.", SettingTypeEnum.Boolean, "false"),
                new PropertySettingDefinition("email.mailgunValidationApiKey", email, "Mailgun validation API key", "Empty uses the sending API key. Stored encrypted.", SettingTypeEnum.Secret),
                new PropertySettingDefinition("email.smtpHost", email, "SMTP host", "This property's own SMTP server (fallback provider).", SettingTypeEnum.String, maxLength: 253),
                new PropertySettingDefinition("email.smtpPort", email, "SMTP port", "Default 587.", SettingTypeEnum.Integer, "587", null, 1, 65535),
                new PropertySettingDefinition("email.smtpUsername", email, "SMTP username", "Empty sends without authentication.", SettingTypeEnum.String, maxLength: 254),
                new PropertySettingDefinition("email.smtpPassword", email, "SMTP password", "Stored encrypted.", SettingTypeEnum.Secret),
                new PropertySettingDefinition("email.smtpUseTls", email, "SMTP STARTTLS", "Default true.", SettingTypeEnum.Boolean, "true"),
                new PropertySettingDefinition("usage.timeZone", "Usage", "Time zone",
                    "IANA time zone of this property's usage days, weeks, months, and years (for example America/New_York). Default UTC.", SettingTypeEnum.String, "UTC", maxLength: 64)
                {
                    Validator = v => Services.Usage.UsageZone.TryZone(v) == null ? "Must be an IANA time zone id, for example UTC or America/New_York." : null
                },
                new PropertySettingDefinition("usage.weekStartsOn", "Usage", "Week starts on",
                    "First day of a usage week. Default Monday.", SettingTypeEnum.Enum, "Monday", new List<string> { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" })
            };
            all.AddRange(DirectoryPropertySettings.Definitions());
            return all;
        }

        #endregion
    }
}
