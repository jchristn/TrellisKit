namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An event (docs/PHASE4_CONTRACT.md section 2.5): EventSummary plus description, hosts, instructors, and the attendee preview.
    /// </summary>
    public class EventView : EventSummaryView
    {
        #region Public-Members

        /// <summary>
        /// Description (Markdown subset), or null (locked stubs).
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Guests per Going RSVP.
        /// </summary>
        public int MaxGuestsPerRsvp { get; set; } = 0;

        /// <summary>
        /// Hosts (empty on locked stubs).
        /// </summary>
        public List<UserSummary> Hosts { get; set; } = new List<UserSummary>();

        /// <summary>
        /// Directory instructors the caller can read.
        /// </summary>
        public List<EventInstructorView> Instructors { get; set; } = new List<EventInstructorView>();

        /// <summary>
        /// Bounded preview when viewer.canSeeAttendees, else null.
        /// </summary>
        public EventAttendeePreviewView? AttendeePreview { get; set; } = null;

        /// <summary>
        /// The proposer's note of a member proposal (event staff and the proposer), else null.
        /// </summary>
        public string? ProposalNote { get; set; } = null;

        #endregion
    }
}
