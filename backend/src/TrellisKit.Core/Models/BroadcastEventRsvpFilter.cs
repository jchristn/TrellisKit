namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// The eventRsvp segment filter: members with an RSVP to an event, optionally in one status (Phase 4 table
    /// eventrsvps).
    /// </summary>
    public class BroadcastEventRsvpFilter
    {
        #region Public-Members

        /// <summary>
        /// Event identifier.
        /// </summary>
        public string EventId { get; set; } = String.Empty;

        /// <summary>
        /// RSVP status (Going, Interested, Waitlisted), or null for any status.
        /// </summary>
        public string? Status { get; set; } = null;

        #endregion
    }
}
