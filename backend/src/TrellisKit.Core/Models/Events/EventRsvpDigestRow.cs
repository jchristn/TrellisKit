namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// An event with RSVP changes in a staff digest window.
    /// </summary>
    public class EventRsvpDigestRow
    {
        #region Public-Members

        /// <summary>
        /// Event.
        /// </summary>
        public string EventId { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Going RSVPs.
        /// </summary>
        public int Going { get; set; } = 0;

        /// <summary>
        /// Waitlisted RSVPs.
        /// </summary>
        public int Waitlisted { get; set; } = 0;

        /// <summary>
        /// RSVPs created or changed in the window.
        /// </summary>
        public long Changes { get; set; } = 0;

        #endregion
    }
}
