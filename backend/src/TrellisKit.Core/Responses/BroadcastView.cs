namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// A broadcast (docs/PHASE5_CONTRACT.md section 2.5).
    /// </summary>
    public class BroadcastView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (bct_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Internal title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Channels.
        /// </summary>
        public ChannelFlags Channels { get; set; } = new ChannelFlags();

        /// <summary>
        /// In-app content.
        /// </summary>
        public BroadcastInAppContent InApp { get; set; } = new BroadcastInAppContent();

        /// <summary>
        /// Push content.
        /// </summary>
        public BroadcastPushContent Push { get; set; } = new BroadcastPushContent();

        /// <summary>
        /// Email content.
        /// </summary>
        public BroadcastEmailContent Email { get; set; } = new BroadcastEmailContent();

        /// <summary>
        /// Audience.
        /// </summary>
        public BroadcastSegmentView Segment { get; set; } = new BroadcastSegmentView();

        /// <summary>
        /// Category.
        /// </summary>
        public BroadcastCategoryEnum Category { get; set; } = BroadcastCategoryEnum.Optional;

        /// <summary>
        /// Recorded reason of an Essential broadcast, or null.
        /// </summary>
        public string? EssentialReason { get; set; } = null;

        /// <summary>
        /// Status.
        /// </summary>
        public BroadcastStatusEnum Status { get; set; } = BroadcastStatusEnum.Draft;

        /// <summary>
        /// Scheduled time, or null.
        /// </summary>
        public DateTime? SendAtUtc { get; set; } = null;

        /// <summary>
        /// Delivery counters.
        /// </summary>
        public BroadcastStats Stats { get; set; } = new BroadcastStats();

        /// <summary>
        /// Creator.
        /// </summary>
        public UserSummary? CreatedBy { get; set; } = null;

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

        /// <summary>
        /// When sending started, or null.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// When every batch finished, or null.
        /// </summary>
        public DateTime? SentUtc { get; set; } = null;

        /// <summary>
        /// When it was canceled, or null.
        /// </summary>
        public DateTime? CanceledUtc { get; set; } = null;

        #endregion
    }
}
