namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An event for event staff (AdminEvent: Event plus staff fields).
    /// </summary>
    public class AdminEventView : EventView
    {
        #region Public-Members

        /// <summary>
        /// Open reports.
        /// </summary>
        public int OpenReportCount { get; set; } = 0;

        /// <summary>
        /// Checked-in RSVPs.
        /// </summary>
        public int CheckedInCount { get; set; } = 0;

        /// <summary>
        /// Guests of Going RSVPs.
        /// </summary>
        public int GuestCount { get; set; } = 0;

        /// <summary>
        /// Reminder states.
        /// </summary>
        public List<EventReminderStatusView> ReminderStatus { get; set; } = new List<EventReminderStatusView>();

        /// <summary>
        /// The stored join URL (always shown to staff).
        /// </summary>
        public string? OnlineJoinUrl { get; set; } = null;

        /// <summary>
        /// Directory venue id, or null.
        /// </summary>
        public string? VenueId { get; set; } = null;

        /// <summary>
        /// Host member ids.
        /// </summary>
        public List<string> HostUserIds { get; set; } = new List<string>();

        /// <summary>
        /// Instructor ids.
        /// </summary>
        public List<string> InstructorIds { get; set; } = new List<string>();

        /// <summary>
        /// Proposer of a member proposal, or null.
        /// </summary>
        public UserSummary? ProposedBy { get; set; } = null;

        /// <summary>
        /// Reviewer's note, or null.
        /// </summary>
        public string? ReviewNote { get; set; } = null;

        /// <summary>
        /// When reviewed, or null.
        /// </summary>
        public DateTime? ReviewedUtc { get; set; } = null;

        /// <summary>
        /// Moderation state.
        /// </summary>
        public ModerationStateEnum ModerationState { get; set; } = ModerationStateEnum.None;

        /// <summary>
        /// Whether the caller is a host (and not event staff).
        /// </summary>
        public bool IsHost { get; set; } = false;

        #endregion
    }
}
