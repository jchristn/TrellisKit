namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of the Messaging moderation actions.
    /// </summary>
    public class MessagingModerationRequest
    {
        #region Public-Members

        /// <summary>
        /// Reason (recorded in the audit log and the moderation notice).
        /// </summary>
        public string? Reason { get; set; } = null;

        #endregion
    }
}
