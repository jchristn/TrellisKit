namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A staff action on an RSVP.
    /// </summary>
    public class EventRsvpActionRequest
    {
        #region Public-Members

        /// <summary>
        /// Reason (0 to 500), or null.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Notify the member (default true).
        /// </summary>
        public bool? NotifyMember { get; set; } = null;

        #endregion
    }
}
