namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Bounded attendee preview of an event.
    /// </summary>
    public class EventAttendeePreviewView
    {
        #region Public-Members

        /// <summary>
        /// At most 10 Going members.
        /// </summary>
        public List<UserSummary> Objects { get; set; } = new List<UserSummary>();

        /// <summary>
        /// Going members the caller may see in the list.
        /// </summary>
        public long TotalRecords { get; set; } = 0;

        /// <summary>
        /// Path of the full list.
        /// </summary>
        public string Enumerate { get; set; } = String.Empty;

        #endregion
    }
}
