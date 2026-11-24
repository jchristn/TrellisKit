namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of PUT /api/v1/admin/email/staff-events/{template}.
    /// </summary>
    public class EmailStaffEventRequest
    {
        #region Public-Members

        /// <summary>
        /// Enabled (unchanged when null).
        /// </summary>
        public bool? Enabled { get; set; } = null;

        /// <summary>
        /// Addresses, at most 10 (unchanged when null; empty uses the defaults).
        /// </summary>
        public List<string>? Recipients { get; set; } = null;

        #endregion
    }
}
