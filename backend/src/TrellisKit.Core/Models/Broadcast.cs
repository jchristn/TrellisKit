namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A staff announcement over in-app, push, and email (docs/PHASE5_CONTRACT.md sections 2.5 and 8). The audience is
    /// the broadcast's own filter rows (a saved segment is copied in when chosen, so later segment edits never change a
    /// scheduled audience).
    /// </summary>
    public class Broadcast
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
        /// Whether the in-app channel is used.
        /// </summary>
        public bool InApp { get; set; } = true;

        /// <summary>
        /// Whether the push channel is used.
        /// </summary>
        public bool Push { get; set; } = false;

        /// <summary>
        /// Whether the email channel is used.
        /// </summary>
        public bool Email { get; set; } = false;

        /// <summary>
        /// Announcement title, or null for the internal title.
        /// </summary>
        public string? InAppTitle { get; set; } = null;

        /// <summary>
        /// Announcement body (Markdown subset), or null.
        /// </summary>
        public string? InAppBody { get; set; } = null;

        /// <summary>
        /// Push title, or null.
        /// </summary>
        public string? PushTitle { get; set; } = null;

        /// <summary>
        /// Push body, or null.
        /// </summary>
        public string? PushBody { get; set; } = null;

        /// <summary>
        /// Email subject, or null.
        /// </summary>
        public string? EmailSubject { get; set; } = null;

        /// <summary>
        /// Email preheader, or null.
        /// </summary>
        public string? EmailPreheader { get; set; } = null;

        /// <summary>
        /// Email body (Markdown subset), or null.
        /// </summary>
        public string? EmailBody { get; set; } = null;

        /// <summary>
        /// Email button label, or null.
        /// </summary>
        public string? CtaLabel { get; set; } = null;

        /// <summary>
        /// Email button path (relative to the property's web origin), or null for the announcement view.
        /// </summary>
        public string? CtaPath { get; set; } = null;

        /// <summary>
        /// The saved segment the filters were copied from, or null for an inline segment.
        /// </summary>
        public string? SegmentId { get; set; } = null;

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
        /// Whether this is the hidden copy of a test send.
        /// </summary>
        public bool IsTest { get; set; } = false;

        /// <summary>
        /// Scheduled time, or null.
        /// </summary>
        public DateTime? SendAtUtc { get; set; } = null;

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

        /// <summary>
        /// Keyset progress: the last user id expanded, or null.
        /// </summary>
        public string? CursorUserId { get; set; } = null;

        /// <summary>
        /// Delivery counters.
        /// </summary>
        public BroadcastStats Stats
        {
            get { return _Stats; }
            set { _Stats = value ?? new BroadcastStats(); }
        }

        /// <summary>
        /// Creator, or null.
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// Audience filter rows (bounded by the segment filter limit).
        /// </summary>
        public List<BroadcastSegmentFilter> Filters
        {
            get { return _Filters; }
            set { _Filters = value ?? new List<BroadcastSegmentFilter>(); }
        }

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private BroadcastStats _Stats = new BroadcastStats();
        private List<BroadcastSegmentFilter> _Filters = new List<BroadcastSegmentFilter>();

        #endregion
    }
}
