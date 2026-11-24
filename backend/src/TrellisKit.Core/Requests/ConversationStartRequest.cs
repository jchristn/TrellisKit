namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Body of POST /api/v1/assistants/{id}/conversations.
    /// </summary>
    public class ConversationStartRequest
    {
        #region Public-Members

        /// <summary>
        /// Optional first message (sent in the same call).
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Idempotency UUID.
        /// </summary>
        public string? ClientMessageId { get; set; } = null;

        #endregion
    }
}
