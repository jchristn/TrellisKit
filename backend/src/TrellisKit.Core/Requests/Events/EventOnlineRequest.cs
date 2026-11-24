namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Online details of an Online or Hybrid event.
    /// </summary>
    public class EventOnlineRequest
    {
        #region Public-Members

        /// <summary>
        /// Platform label (0 to 60), for example Video call.
        /// </summary>
        public string? PlatformLabel { get; set; } = null;

        /// <summary>
        /// Join URL (https only).
        /// </summary>
        public string? JoinUrl { get; set; } = null;

        #endregion
    }
}
