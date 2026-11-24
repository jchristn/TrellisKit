namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// POST /api/v1/admin/broadcasts/{id}/send body.
    /// </summary>
    public class BroadcastSendRequest
    {
        #region Public-Members

        /// <summary>
        /// The audience count the sender confirmed; must be within 5 percent of the current preview count.
        /// </summary>
        public long? ConfirmCount { get; set; } = null;

        #endregion
    }
}
