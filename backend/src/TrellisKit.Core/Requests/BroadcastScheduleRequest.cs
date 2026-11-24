namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// POST /api/v1/admin/broadcasts/{id}/schedule body.
    /// </summary>
    public class BroadcastScheduleRequest
    {
        #region Public-Members

        /// <summary>
        /// When to send: at least 5 minutes and at most 90 days ahead.
        /// </summary>
        public DateTime? SendAtUtc { get; set; } = null;

        #endregion
    }
}
