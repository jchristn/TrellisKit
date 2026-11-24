namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One recipient of a broadcast with a status per channel.
    /// </summary>
    public class BroadcastDeliveryView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (bdl_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Broadcast.
        /// </summary>
        public string BroadcastId { get; set; } = String.Empty;

        /// <summary>
        /// Recipient.
        /// </summary>
        public UserSummary User { get; set; } = new UserSummary();

        /// <summary>
        /// In-app status.
        /// </summary>
        public BroadcastDeliveryStatusEnum InAppStatus { get; set; } = BroadcastDeliveryStatusEnum.NotRequested;

        /// <summary>
        /// Push status.
        /// </summary>
        public BroadcastDeliveryStatusEnum PushStatus { get; set; } = BroadcastDeliveryStatusEnum.NotRequested;

        /// <summary>
        /// Email status.
        /// </summary>
        public BroadcastDeliveryStatusEnum EmailStatus { get; set; } = BroadcastDeliveryStatusEnum.NotRequested;

        /// <inheritdoc />
        public List<string> Labels { get; set; } = new List<string>();

        /// <inheritdoc />
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
