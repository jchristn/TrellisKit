namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Duplicate an event as a Draft.
    /// </summary>
    public class EventDuplicateRequest
    {
        #region Public-Members

        /// <summary>
        /// Local start of the copy (yyyy-MM-ddTHH:mm).
        /// </summary>
        public string? StartLocal { get; set; } = null;

        /// <summary>
        /// IANA zone of the copy (default the original's).
        /// </summary>
        public string? TimeZone { get; set; } = null;

        #endregion
    }
}
