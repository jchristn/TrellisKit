namespace TrellisKit.Core.Settings
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Outbound email (SMTP). Amazon SES is used through its SMTP interface in production. An empty host disables sending (messages are logged instead).
    /// </summary>
    public class EmailSettings
    {
        #region Public-Members

        /// <summary>
        /// SMTP host, for example email-smtp.us-west-2.amazonaws.com or mailpit.
        /// </summary>
        public string SmtpHost { get; set; } = String.Empty;

        /// <summary>
        /// Whether to use STARTTLS.
        /// </summary>
        public bool UseTls { get; set; } = true;

        /// <summary>
        /// SMTP username.
        /// </summary>
        public string Username { get; set; } = String.Empty;

        /// <summary>
        /// SMTP password. Secret.
        /// </summary>
        public string Password { get; set; } = String.Empty;

        /// <summary>
        /// From address. Default empty: use the property contact.emailFromAddress.
        /// </summary>
        public string FromAddress { get; set; } = String.Empty;

        /// <summary>
        /// From display name. Default empty: use the property contact.emailFromName.
        /// </summary>
        public string FromName { get; set; } = String.Empty;

        /// <summary>
        /// SMTP port. Default 587. Minimum 1, maximum 65535.
        /// </summary>
        public int SmtpPort
        {
            get { return _SmtpPort; }
            set { _SmtpPort = Math.Clamp(value, 1, 65535); }
        }

        /// <summary>
        /// Email members about new feed posts (opt-out per member). Default true.
        /// </summary>
        public bool FeedPostEmailsEnabled { get; set; } = true;

        /// <summary>
        /// At most one feed post email per member in this time. 0 sends one for every post. Default 0. Minimum 0, maximum 10080.
        /// </summary>
        public int FeedPostMinIntervalMinutes
        {
            get { return _FeedPostMinIntervalMinutes; }
            set { _FeedPostMinIntervalMinutes = Math.Clamp(value, 0, 10080); }
        }

        /// <summary>
        /// Emails sent per fan-out batch. Default 100. Minimum 10, maximum 1000.
        /// </summary>
        public int FeedPostBatchSize
        {
            get { return _FeedPostBatchSize; }
            set { _FeedPostBatchSize = Math.Clamp(value, 10, 1000); }
        }

        /// <summary>
        /// Deployment-level provider: platform administrator mail in MultiProperty, and the one property's default in
        /// SingleProperty (a property's own email settings win). Default Auto.
        /// </summary>
        public EmailProviderEnum Provider { get; set; } = EmailProviderEnum.Auto;

        /// <summary>
        /// Deployment-level Mailgun sending domain (platform administrator mail only; properties configure their own in
        /// the admin portal). Default empty.
        /// </summary>
        public string MailgunDomain { get; set; } = String.Empty;

        /// <summary>
        /// Deployment-level Mailgun API key (platform administrator mail only). Default empty.
        /// </summary>
        public string MailgunApiKey { get; set; } = String.Empty;

        /// <summary>
        /// Deployment-level Mailgun API base URL (US https://api.mailgun.net/v3/ or EU https://api.eu.mailgun.net/v3/).
        /// Default US.
        /// </summary>
        public string MailgunBaseUrl
        {
            get { return _MailgunBaseUrl; }
            set { _MailgunBaseUrl = String.IsNullOrWhiteSpace(value) ? DefaultMailgunBaseUrl : value.Trim(); }
        }

        /// <summary>
        /// Most delivery attempts per message before it is marked Failed. Default 5. Minimum 1, maximum 20.
        /// </summary>
        public int DeliveryMaxAttempts
        {
            get { return _DeliveryMaxAttempts; }
            set { _DeliveryMaxAttempts = Math.Clamp(value, 1, 20); }
        }

        /// <summary>
        /// Interval of the delivery worker that retries queued messages, in seconds. Default 30. Minimum 5, maximum 3600.
        /// </summary>
        public int DeliveryIntervalSeconds
        {
            get { return _DeliveryIntervalSeconds; }
            set { _DeliveryIntervalSeconds = Math.Clamp(value, 5, 3600); }
        }

        /// <summary>
        /// Minimum minutes between two integration failure alerts of the same kind to one property. Default 60. Minimum
        /// 1, maximum 1440.
        /// </summary>
        public int IntegrationAlertMinutes
        {
            get { return _IntegrationAlertMinutes; }
            set { _IntegrationAlertMinutes = Math.Clamp(value, 1, 1440); }
        }

        /// <summary>
        /// Days email message records are kept. Default 90. Minimum 1, maximum 3650.
        /// </summary>
        public int MessageRetentionDays
        {
            get { return _MessageRetentionDays; }
            set { _MessageRetentionDays = Math.Clamp(value, 1, 3650); }
        }

        /// <summary>
        /// Default US Mailgun API base URL.
        /// </summary>
        public const string DefaultMailgunBaseUrl = "https://api.mailgun.net/v3/";

        #endregion

        #region Private-Members

        private int _SmtpPort = 587;
        private int _FeedPostMinIntervalMinutes = 0;
        private int _FeedPostBatchSize = 100;
        private string _MailgunBaseUrl = DefaultMailgunBaseUrl;
        private int _DeliveryMaxAttempts = 5;
        private int _DeliveryIntervalSeconds = 30;
        private int _IntegrationAlertMinutes = 60;
        private int _MessageRetentionDays = 90;

        #endregion
    }
}
