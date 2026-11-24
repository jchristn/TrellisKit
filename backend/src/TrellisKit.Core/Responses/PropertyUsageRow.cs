namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One property in the platform usage comparison (docs/USAGE_METERING_CONTRACT.md section 7.2). Every value is the period total in the property time zone.
    /// </summary>
    public class PropertyUsageRow
    {
        #region Public-Members

        /// <summary>
        /// Property id.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Property slug.
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Property name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Registry status.
        /// </summary>
        public PropertyStatusEnum Status { get; set; } = PropertyStatusEnum.Disabled;

        /// <summary>
        /// Period.
        /// </summary>
        public UsagePeriodEnum Period { get; set; } = UsagePeriodEnum.LastMonth;

        /// <summary>
        /// Period start in the property time zone.
        /// </summary>
        public DateTime FromUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Period end.
        /// </summary>
        public DateTime ToUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// users.total at the period end.
        /// </summary>
        public long UsersTotal { get; set; } = 0;

        /// <summary>
        /// users.new.
        /// </summary>
        public long UsersNew { get; set; } = 0;

        /// <summary>
        /// users.active (distinct in the period).
        /// </summary>
        public long ActiveUsers { get; set; } = 0;

        /// <summary>
        /// posts.new.
        /// </summary>
        public long Posts { get; set; } = 0;

        /// <summary>
        /// comments.new.
        /// </summary>
        public long Comments { get; set; } = 0;

        /// <summary>
        /// interactions.new.
        /// </summary>
        public long Interactions { get; set; } = 0;

        /// <summary>
        /// images.new.
        /// </summary>
        public long Images { get; set; } = 0;

        /// <summary>
        /// images.bytes.
        /// </summary>
        public long ImageBytes { get; set; } = 0;

        /// <summary>
        /// videos.new.
        /// </summary>
        public long Videos { get; set; } = 0;

        /// <summary>
        /// videos.bytes.
        /// </summary>
        public long VideoBytes { get; set; } = 0;

        /// <summary>
        /// storage.total at the period end.
        /// </summary>
        public long StorageBytes { get; set; } = 0;

        /// <summary>
        /// api.requests.
        /// </summary>
        public long ApiRequests { get; set; } = 0;

        /// <summary>
        /// push.sent.
        /// </summary>
        public long PushSent { get; set; } = 0;

        /// <summary>
        /// email.sent.
        /// </summary>
        public long EmailSent { get; set; } = 0;

        /// <summary>
        /// ai.promptTokens plus ai.completionTokens.
        /// </summary>
        public long AiTokens { get; set; } = 0;

        /// <summary>
        /// ai.costMicros.
        /// </summary>
        public long AiCostMicros { get; set; } = 0;

        /// <summary>
        /// When the property last rolled up, or null.
        /// </summary>
        public DateTime? DataThroughUtc { get; set; } = null;

        /// <summary>
        /// Property labels. Never null.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Property tags. Never null.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
