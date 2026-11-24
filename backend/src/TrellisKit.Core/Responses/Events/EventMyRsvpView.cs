namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The caller's RSVP.
    /// </summary>
    public class EventMyRsvpView
    {
        #region Public-Members

        /// <summary>
        /// Identifier (rsv_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Status.
        /// </summary>
        public EventRsvpStatusEnum Status { get; set; } = EventRsvpStatusEnum.Going;

        /// <summary>
        /// Guests.
        /// </summary>
        public int GuestCount { get; set; } = 0;

        /// <summary>
        /// Waitlist position (1 based) when Waitlisted.
        /// </summary>
        public int? Position { get; set; } = null;

        /// <summary>
        /// Hidden from attendee lists.
        /// </summary>
        public bool HideFromAttendeeLists { get; set; } = false;

        /// <summary>
        /// Check-in time, or null.
        /// </summary>
        public DateTime? CheckedInUtc { get; set; } = null;

        /// <summary>
        /// Whether the event was canceled (the RSVP is kept for history).
        /// </summary>
        public bool EventCanceled { get; set; } = false;

        /// <summary>
        /// Updated.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
