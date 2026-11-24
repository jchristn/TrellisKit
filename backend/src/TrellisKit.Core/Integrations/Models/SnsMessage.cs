namespace TrellisKit.Core.Integrations.Models
{
    using System;

    /// <summary>
    /// An Amazon SNS HTTP(S) message (Notification, SubscriptionConfirmation, or UnsubscribeConfirmation).
    /// </summary>
    public class SnsMessage
    {
        #region Public-Members

        /// <summary>
        /// Message type.
        /// </summary>
        public string Type { get; set; } = String.Empty;

        /// <summary>
        /// Message identifier.
        /// </summary>
        public string MessageId { get; set; } = String.Empty;

        /// <summary>
        /// Topic ARN.
        /// </summary>
        public string TopicArn { get; set; } = String.Empty;

        /// <summary>
        /// Subject, or null.
        /// </summary>
        public string? Subject { get; set; } = null;

        /// <summary>
        /// Message body.
        /// </summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>
        /// Timestamp text exactly as sent.
        /// </summary>
        public string Timestamp { get; set; } = String.Empty;

        /// <summary>
        /// Signature version: 1 (SHA1) or 2 (SHA256).
        /// </summary>
        public string SignatureVersion { get; set; } = String.Empty;

        /// <summary>
        /// Base64 signature.
        /// </summary>
        public string Signature { get; set; } = String.Empty;

        /// <summary>
        /// URL of the signing certificate.
        /// </summary>
        public string SigningCertURL { get; set; } = String.Empty;

        /// <summary>
        /// Subscription confirmation URL, or null.
        /// </summary>
        public string? SubscribeURL { get; set; } = null;

        /// <summary>
        /// Subscription token, or null.
        /// </summary>
        public string? Token { get; set; } = null;

        /// <summary>
        /// Unsubscribe URL, or null.
        /// </summary>
        public string? UnsubscribeURL { get; set; } = null;

        #endregion
    }
}
