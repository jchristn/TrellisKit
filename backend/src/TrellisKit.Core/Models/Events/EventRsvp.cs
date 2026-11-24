namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An RSVP (docs/PHASE4_CONTRACT.md section 4.3).
    /// </summary>
    public class EventRsvp
    {
        #region Public-Members

        /// <summary>
        /// Identifier (rsv_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Event.
        /// </summary>
        public string EventId { get; set; } = String.Empty;

        /// <summary>
        /// Member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Status.
        /// </summary>
        public EventRsvpStatusEnum Status { get; set; } = EventRsvpStatusEnum.Going;

        /// <summary>
        /// Guests.
        /// </summary>
        public int GuestCount { get; set; } = 0;

        /// <summary>
        /// Hidden from attendee lists for everyone except event staff and hosts.
        /// </summary>
        public bool HideFromAttendeeLists { get; set; } = false;

        /// <summary>
        /// When waitlisted (the waitlist order), or null.
        /// </summary>
        public DateTime? WaitlistedUtc { get; set; } = null;

        /// <summary>
        /// When last promoted from the waitlist, or null.
        /// </summary>
        public DateTime? PromotedUtc { get; set; } = null;

        /// <summary>
        /// When checked in, or null.
        /// </summary>
        public DateTime? CheckedInUtc { get; set; } = null;

        /// <summary>
        /// Who checked the member in, or null.
        /// </summary>
        public string? CheckedInByUserId { get; set; } = null;

        /// <summary>
        /// Waitlist position (1 based) when Waitlisted and computed by the read, else null.
        /// </summary>
        public int? Position { get; set; } = null;

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
