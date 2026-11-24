namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One RSVP in the account export (docs/PHASE4_CONTRACT.md section 10).
    /// </summary>
    public class EventRsvpExportItem
    {
        #region Public-Members

        /// <summary>
        /// RSVP identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Event.
        /// </summary>
        public string EventId { get; set; } = String.Empty;

        /// <summary>
        /// Event title.
        /// </summary>
        public string EventTitle { get; set; } = String.Empty;

        /// <summary>
        /// Event start.
        /// </summary>
        public DateTime EventStartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Event status.
        /// </summary>
        public EventStatusEnum EventStatus { get; set; } = EventStatusEnum.Published;

        /// <summary>
        /// RSVP status.
        /// </summary>
        public EventRsvpStatusEnum Status { get; set; } = EventRsvpStatusEnum.Going;

        /// <summary>
        /// Guests.
        /// </summary>
        public int GuestCount { get; set; } = 0;

        /// <summary>
        /// Hidden from attendee lists.
        /// </summary>
        public bool HideFromAttendeeLists { get; set; } = false;

        /// <summary>
        /// Check-in time, or null.
        /// </summary>
        public DateTime? CheckedInUtc { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
