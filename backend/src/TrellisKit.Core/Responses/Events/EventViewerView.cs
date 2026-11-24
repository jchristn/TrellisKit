namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// What the caller may do with an event (docs/PHASE4_CONTRACT.md section 1.3).
    /// </summary>
    public class EventViewerView
    {
        #region Public-Members

        /// <summary>
        /// May RSVP now.
        /// </summary>
        public bool CanRsvp { get; set; } = false;

        /// <summary>
        /// May RSVP Going now (capacity or waitlist).
        /// </summary>
        public bool CanRsvpGoing { get; set; } = false;

        /// <summary>
        /// A Going RSVP would be waitlisted.
        /// </summary>
        public bool CanJoinWaitlist { get; set; } = false;

        /// <summary>
        /// May comment.
        /// </summary>
        public bool CanComment { get; set; } = false;

        /// <summary>
        /// May react.
        /// </summary>
        public bool CanReact { get; set; } = false;

        /// <summary>
        /// May report.
        /// </summary>
        public bool CanReport { get; set; } = false;

        /// <summary>
        /// May list attendees.
        /// </summary>
        public bool CanSeeAttendees { get; set; } = false;

        /// <summary>
        /// May edit (event staff all fields; hosts description, cover, and labels).
        /// </summary>
        public bool CanEdit { get; set; } = false;

        /// <summary>
        /// May cancel.
        /// </summary>
        public bool CanCancel { get; set; } = false;

        /// <summary>
        /// May check people in.
        /// </summary>
        public bool CanCheckIn { get; set; } = false;

        /// <summary>
        /// May promote to the feed.
        /// </summary>
        public bool CanPromote { get; set; } = false;

        /// <summary>
        /// Whether the event is readable (false for a locked stub).
        /// </summary>
        public bool CanOpen { get; set; } = true;

        /// <summary>
        /// SignIn, Membership, or null.
        /// </summary>
        public string? Requires { get; set; } = null;

        #endregion
    }
}
