namespace TrellisKit.Core.Integrations.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// An outbound email.
    /// </summary>
    public class EmailMessage
    {
        #region Public-Members

        /// <summary>
        /// Recipient address.
        /// </summary>
        public string To { get; set; } = String.Empty;

        /// <summary>
        /// Subject line.
        /// </summary>
        public string Subject { get; set; } = String.Empty;

        /// <summary>
        /// Plain-text body.
        /// </summary>
        public string TextBody { get; set; } = String.Empty;

        /// <summary>
        /// HTML body, or null for text only.
        /// </summary>
        public string? HtmlBody { get; set; } = null;

        /// <summary>
        /// Low-cardinality category for telemetry, for example verify_email or password_reset.
        /// </summary>
        public string Category { get; set; } = "general";

        /// <summary>
        /// Sender name for this message, or null for the sender's configured name.
        /// </summary>
        public string? FromName { get; set; } = null;

        /// <summary>
        /// Sender address for this message, or null for the sender's configured address.
        /// </summary>
        public string? FromAddress { get; set; } = null;

        /// <summary>
        /// Reply-to address, or null.
        /// </summary>
        public string? ReplyTo { get; set; } = null;

        /// <summary>
        /// Provider message id set by the sender after a successful send (Mailgun), or null.
        /// </summary>
        public string? ProviderMessageId { get; set; } = null;

        /// <summary>
        /// Extra message headers (for example List-Unsubscribe and List-Unsubscribe-Post), by name. Never null.
        /// </summary>
        public Dictionary<string, string> Headers
        {
            get { return _Headers; }
            set { _Headers = value ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
        }

        /// <summary>
        /// Provider tags for analytics and filtering (Mailgun o:tag), for example the template key and category. At most
        /// <see cref="MaxTags"/> are sent; each is trimmed to at most 128 characters. Never null. SMTP ignores tags.
        /// </summary>
        public List<string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new List<string>(); }
        }

        /// <summary>
        /// Provider variables carried with the message and echoed back in webhooks (Mailgun v:name), for example the
        /// engine's message record id. Never null. SMTP ignores variables.
        /// </summary>
        public Dictionary<string, string> Variables
        {
            get { return _Variables; }
            set { _Variables = value ?? new Dictionary<string, string>(StringComparer.Ordinal); }
        }

        /// <summary>
        /// Whether the provider may track opens (Mailgun o:tracking-opens), or null for the sending domain's default.
        /// Default null. SMTP ignores it.
        /// </summary>
        public bool? TrackOpens { get; set; } = null;

        /// <summary>
        /// Whether the provider may rewrite links to track clicks (Mailgun o:tracking-clicks), or null for the sending
        /// domain's default. Default null. SMTP ignores it.
        /// </summary>
        public bool? TrackClicks { get; set; } = null;

        /// <summary>
        /// Most provider tags sent per message (Mailgun accepts three).
        /// </summary>
        public const int MaxTags = 3;

        #endregion

        #region Private-Members

        private Dictionary<string, string> _Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private List<string> _Tags = new List<string>();
        private Dictionary<string, string> _Variables = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion
    }
}
