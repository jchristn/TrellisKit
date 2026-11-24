namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A registered property (PropertyRecord in docs/MULTIPROPERTY_CONTRACT.md section 6.3). domains (at most 20) and apps (at most 10) are fixed-bound lists.
    /// </summary>
    public class PropertyRecordResponse
    {
        #region Public-Members

        /// <summary>
        /// Opaque property id.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Cosmetic slug.
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Display name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Status.
        /// </summary>
        public PropertyStatusEnum Status { get; set; } = PropertyStatusEnum.Disabled;

        /// <summary>
        /// Domains (at most 20).
        /// </summary>
        public List<PlatformDomainResponse> Domains { get; set; } = new List<PlatformDomainResponse>();

        /// <summary>
        /// Apps (at most 10).
        /// </summary>
        public List<PlatformAppResponse> Apps { get; set; } = new List<PlatformAppResponse>();

        /// <summary>
        /// Non-deleted member accounts.
        /// </summary>
        public long MemberCount { get; set; } = 0;

        /// <summary>
        /// Created time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last change.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last enabled.
        /// </summary>
        public DateTime? EnabledUtc { get; set; } = null;

        /// <summary>
        /// Last disabled.
        /// </summary>
        public DateTime? DisabledUtc { get; set; } = null;

        /// <summary>
        /// Reason given when disabled.
        /// </summary>
        public string? DisabledReason { get; set; } = null;

        /// <summary>
        /// When the purge may start (Deleting).
        /// </summary>
        public DateTime? DeleteScheduledUtc { get; set; } = null;

        /// <summary>
        /// When the purge started.
        /// </summary>
        public DateTime? PurgeStartedUtc { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
