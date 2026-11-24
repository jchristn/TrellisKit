namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of PUT /api/v1/conversations/{id}/mute.
    /// </summary>
    public class ConversationMuteRequest
    {
        #region Public-Members

        /// <summary>
        /// Mute end, or null for indefinitely.
        /// </summary>
        public DateTime? UntilUtc { get; set; } = null;

        #endregion
    }
}
