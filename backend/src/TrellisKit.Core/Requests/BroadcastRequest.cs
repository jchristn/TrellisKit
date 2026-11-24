namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// POST /api/v1/admin/broadcasts and PUT /api/v1/admin/broadcasts/{id} body (docs/PHASE5_CONTRACT.md section 2.5).
    /// On update, omitted fields keep their values.
    /// </summary>
    public class BroadcastRequest
    {
        #region Public-Members

        /// <summary>
        /// Internal title (1 to 200 characters).
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Channels (default in-app only). Push rides on the in-app notification, so push needs inApp.
        /// </summary>
        public ChannelFlags? Channels { get; set; } = null;

        /// <summary>
        /// In-app content.
        /// </summary>
        public BroadcastInAppContent? InApp { get; set; } = null;

        /// <summary>
        /// Push content.
        /// </summary>
        public BroadcastPushContent? Push { get; set; } = null;

        /// <summary>
        /// Email content.
        /// </summary>
        public BroadcastEmailContent? Email { get; set; } = null;

        /// <summary>
        /// A saved segment to copy the audience from (exclusive with segment).
        /// </summary>
        public string? SegmentId { get; set; } = null;

        /// <summary>
        /// An inline segment (exclusive with segmentId).
        /// </summary>
        public BroadcastSegmentRequest? Segment { get; set; } = null;

        /// <summary>
        /// Category (default Optional). Essential is SystemAdmin only and needs essentialReason.
        /// </summary>
        public BroadcastCategoryEnum? Category { get; set; } = null;

        /// <summary>
        /// Reason of an Essential broadcast (10 to 500 characters).
        /// </summary>
        public string? EssentialReason { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
