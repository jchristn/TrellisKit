namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Online details of an event.
    /// </summary>
    public class EventOnlineView
    {
        #region Public-Members

        /// <summary>
        /// Platform label.
        /// </summary>
        public string? PlatformLabel { get; set; } = null;

        /// <summary>
        /// Join URL: only for Going members, event staff, and hosts, from joinUrlAvailableUtc; null otherwise.
        /// </summary>
        public string? JoinUrl { get; set; } = null;

        /// <summary>
        /// When the join URL becomes available.
        /// </summary>
        public DateTime? JoinUrlAvailableUtc { get; set; } = null;

        #endregion
    }
}
