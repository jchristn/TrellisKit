namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// RSVP to an event (docs/PHASE4_CONTRACT.md section 4.1).
    /// </summary>
    public class EventRsvpRequest
    {
        #region Public-Members

        /// <summary>
        /// Going or Interested.
        /// </summary>
        public EventRsvpStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// Guests (0 to the event's maxGuestsPerRsvp).
        /// </summary>
        public int? GuestCount { get; set; } = null;

        /// <summary>
        /// Hide yourself from attendee lists (default false).
        /// </summary>
        public bool? HideFromAttendeeLists { get; set; } = null;

        #endregion
    }
}
