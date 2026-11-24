namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One administrative email to queue.
    /// </summary>
    public class EmailRequest
    {
        #region Public-Members

        /// <summary>
        /// Template key.
        /// </summary>
        public string Template { get; set; } = String.Empty;

        /// <summary>
        /// Recipient address.
        /// </summary>
        public string To { get; set; } = String.Empty;

        /// <summary>
        /// Member the message is about, or null.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Token values (the common tokens name, supportEmail, and webUrl are filled in).
        /// </summary>
        public Dictionary<string, string> Tokens
        {
            get { return _Tokens; }
            set { _Tokens = value ?? new Dictionary<string, string>(StringComparer.Ordinal); }
        }

        /// <summary>
        /// Whether the engine adds List-Unsubscribe and List-Unsubscribe-Post to an Optional template's message from its
        /// unsubscribeLink token when <see cref="Headers"/> does not set them. Default true; false for an essential notice
        /// sent with an optional template (for example an essential broadcast).
        /// </summary>
        public bool AddListUnsubscribe { get; set; } = true;

        /// <summary>
        /// Treat this message as Essential even when its template is Optional (essential broadcasts). Default false.
        /// Essential sends disable tracking and automatic unsubscribe headers, including on retries.
        /// </summary>
        public bool Essential { get; set; } = false;

        /// <summary>
        /// Extra headers, for example List-Unsubscribe (sent by SMTP and Mailgun).
        /// </summary>
        public Dictionary<string, string> Headers
        {
            get { return _Headers; }
            set { _Headers = value ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
        }

        /// <summary>
        /// Event key: at most one message per key per property (a duplicate webhook never sends twice), or null.
        /// </summary>
        public string? IdempotencyKey { get; set; } = null;

        #endregion

        #region Private-Members

        private Dictionary<string, string> _Tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        private Dictionary<string, string> _Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        #endregion
    }
}
