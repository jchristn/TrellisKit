namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// POST /api/v1/threads/{id}/lock body.
    /// </summary>
    public class ThreadLockRequest
    {
        #region Public-Members

        /// <summary>
        /// Reason (0 to 500 characters).
        /// </summary>
        public string? Reason { get; set; } = null;

        #endregion
    }
}
