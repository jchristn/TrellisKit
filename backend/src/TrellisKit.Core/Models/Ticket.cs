namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A support ticket (docs/PHASE5_CONTRACT.md sections 2.3 and 16, table tickets).
    /// </summary>
    public class Ticket
    {
        #region Public-Members

        /// <summary>
        /// Identifier (tkt_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Per-property sequential number.
        /// </summary>
        public long Number { get; set; } = 0;

        /// <summary>
        /// Ticket type key.
        /// </summary>
        public string TypeKey { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Opening body (also the first message).
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Status.
        /// </summary>
        public TicketStatusEnum Status { get; set; } = TicketStatusEnum.Open;

        /// <summary>
        /// Priority.
        /// </summary>
        public TicketPriorityEnum Priority { get; set; } = TicketPriorityEnum.Normal;

        /// <summary>
        /// Requester, or null.
        /// </summary>
        public string? RequesterUserId { get; set; } = null;

        /// <summary>
        /// Assignee, or null.
        /// </summary>
        public string? AssigneeUserId { get; set; } = null;

        /// <summary>
        /// Linked idea, or null.
        /// </summary>
        public string? IdeaId { get; set; } = null;

        /// <summary>
        /// Whether the device block was captured.
        /// </summary>
        public bool IncludeDiagnostics { get; set; } = false;

        /// <summary>
        /// X-Client value.
        /// </summary>
        public string? DeviceClient { get; set; } = null;

        /// <summary>
        /// X-Client-Version value.
        /// </summary>
        public string? DeviceClientVersion { get; set; } = null;

        /// <summary>
        /// X-Client-Platform value.
        /// </summary>
        public string? DevicePlatform { get; set; } = null;

        /// <summary>
        /// X-Client-Os-Version value.
        /// </summary>
        public string? DeviceOsVersion { get; set; } = null;

        /// <summary>
        /// X-Client-Device-Model value.
        /// </summary>
        public string? DeviceModel { get; set; } = null;

        /// <summary>
        /// X-Client-Update-Id value.
        /// </summary>
        public string? DeviceUpdateId { get; set; } = null;

        /// <summary>
        /// Locale (Accept-Language or X-Client-Locale), or null.
        /// </summary>
        public string? DeviceLocale { get; set; } = null;

        /// <summary>
        /// Time zone (X-Client-Time-Zone), or null.
        /// </summary>
        public string? DeviceTimeZone { get; set; } = null;

        /// <summary>
        /// Membership snapshot at creation: active.
        /// </summary>
        public bool MembershipActive { get; set; } = false;

        /// <summary>
        /// Membership snapshot at creation: source.
        /// </summary>
        public string? MembershipSource { get; set; } = null;

        /// <summary>
        /// Membership snapshot at creation: store.
        /// </summary>
        public string? MembershipStore { get; set; } = null;

        /// <summary>
        /// Membership snapshot at creation: expiry.
        /// </summary>
        public DateTime? MembershipExpiresUtc { get; set; } = null;

        /// <summary>
        /// First response target.
        /// </summary>
        public DateTime? FirstResponseDueUtc { get; set; } = null;

        /// <summary>
        /// First staff reply.
        /// </summary>
        public DateTime? FirstResponseUtc { get; set; } = null;

        /// <summary>
        /// When TicketOverdue was sent (once).
        /// </summary>
        public DateTime? OverdueNotifiedUtc { get; set; } = null;

        /// <summary>
        /// Last activity.
        /// </summary>
        public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last member reply, or null.
        /// </summary>
        public DateTime? LastMemberReplyUtc { get; set; } = null;

        /// <summary>
        /// Resolved, or null.
        /// </summary>
        public DateTime? ResolvedUtc { get; set; } = null;

        /// <summary>
        /// Closed, or null.
        /// </summary>
        public DateTime? ClosedUtc { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
