namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An RSVP with its event (account exports, my events).
    /// </summary>
    public class EventRsvpRecord
    {
        #region Public-Members

        /// <summary>
        /// RSVP.
        /// </summary>
        public EventRsvp Rsvp { get; set; } = new EventRsvp();

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

        #endregion
    }
}
