namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Body of POST /api/v1/assistant-conversations/{id}/messages.
    /// </summary>
    public class MessageSendRequest
    {
        #region Public-Members

        /// <summary>
        /// Message (1 to limits.maxMessageCharacters).
        /// </summary>
        public string? Content { get; set; } = null;

        /// <summary>
        /// Idempotency UUID.
        /// </summary>
        public string? ClientMessageId { get; set; } = null;

        #endregion
    }
}
