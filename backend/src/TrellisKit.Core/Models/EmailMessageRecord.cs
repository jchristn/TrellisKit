namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Security;

    /// <summary>
    /// An administrative email record (docs/TRELLISKIT.md section 12.4). Bodies are never stored: the queued payload (recipient
    /// and template tokens) is encrypted with the engine secrets key and removed when the message reaches a final status.
    /// </summary>
    public class EmailMessageRecord : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (eml_ prefix).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Template key, for example welcome.
        /// </summary>
        public string Template { get; set; } = String.Empty;

        /// <summary>
        /// Category.
        /// </summary>
        public EmailCategoryEnum Category { get; set; } = EmailCategoryEnum.Essential;

        /// <summary>
        /// Recipient address.
        /// </summary>
        public string Recipient { get; set; } = String.Empty;

        /// <summary>
        /// Member the message is about, or null.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Rendered subject.
        /// </summary>
        public string Subject { get; set; } = String.Empty;

        /// <summary>
        /// Status.
        /// </summary>
        public EmailStatusEnum Status { get; set; } = EmailStatusEnum.Queued;

        /// <summary>
        /// Provider that handled the last attempt (Mailgun, Smtp, Log).
        /// </summary>
        public string? Provider { get; set; } = null;

        /// <summary>
        /// Provider message id, or null.
        /// </summary>
        public string? ProviderMessageId { get; set; } = null;

        /// <summary>
        /// Delivery attempts.
        /// </summary>
        public int Attempts { get; set; } = 0;

        /// <summary>
        /// Next retry (Queued only).
        /// </summary>
        public DateTime? NextAttemptUtc { get; set; } = null;

        /// <summary>
        /// Last delivery error (no credentials), or null.
        /// </summary>
        public string? LastError { get; set; } = null;

        /// <summary>
        /// Event key: one message per key per property. Not serialized.
        /// </summary>
        [JsonIgnore]
        public string? IdempotencyKey { get; set; } = null;

        /// <summary>
        /// Encrypted payload while queued. Not serialized.
        /// </summary>
        [JsonIgnore]
        public SecretCiphertext? Payload { get; set; } = null;

        /// <summary>
        /// Queued (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Accepted by the provider (UTC).
        /// </summary>
        public DateTime? SentUtc { get; set; } = null;

        /// <summary>
        /// Delivery confirmed (UTC).
        /// </summary>
        public DateTime? DeliveredUtc { get; set; } = null;

        /// <summary>
        /// Failed (UTC).
        /// </summary>
        public DateTime? FailedUtc { get; set; } = null;

        /// <summary>
        /// Updated (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(StringComparer.Ordinal); }
        }

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion
    }
}
