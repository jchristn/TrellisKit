namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Publish an event.
    /// </summary>
    public class EventPublishRequest
    {
        #region Public-Members

        /// <summary>
        /// Run a NewEvent fan-out to every member who may read it (default false).
        /// </summary>
        public bool? NotifyMembers { get; set; } = null;

        #endregion
    }
}
