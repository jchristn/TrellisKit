namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// POST /api/v1/admin/broadcasts/{id}/test body.
    /// </summary>
    public class BroadcastTestRequest
    {
        #region Public-Members

        /// <summary>
        /// Channels to test (inApp, push, email; push needs inApp), or null or empty for the broadcast's channels.
        /// </summary>
        public List<string>? Channels { get; set; } = null;

        #endregion
    }
}
