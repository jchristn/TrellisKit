namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A subscription event in the admin detail.
    /// </summary>
    public class SubscriptionEventView
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Source.
        /// </summary>
        public SubscriptionEventSourceEnum Source { get; set; } = SubscriptionEventSourceEnum.Webhook;

        /// <summary>
        /// Store event type.
        /// </summary>
        public string Type { get; set; } = String.Empty;

        /// <summary>
        /// Received.
        /// </summary>
        public DateTime ReceivedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Processed.
        /// </summary>
        public DateTime? ProcessedUtc { get; set; } = null;

        /// <summary>
        /// Outcome.
        /// </summary>
        public string Outcome { get; set; } = String.Empty;

        /// <summary>
        /// Raw store payload (unmanaged JSON).
        /// </summary>
        public string? RawPayload { get; set; } = null;

        #endregion
    }
}
