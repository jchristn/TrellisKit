namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Cancel an event (docs/PHASE4_CONTRACT.md section 4.4).
    /// </summary>
    public class EventCancelRequest
    {
        #region Public-Members

        /// <summary>
        /// Reason (3 to 500).
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Send EventCanceled to RSVPed members (default true).
        /// </summary>
        public bool? NotifyAttendees { get; set; } = null;

        /// <summary>
        /// Occurrences: One (default), Following, or All.
        /// </summary>
        public EventEditScopeEnum? Scope { get; set; } = null;

        #endregion
    }
}
