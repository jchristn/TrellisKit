namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of POST /api/v1/conversations/{id}/messages.
    /// </summary>
    public class DirectMessageSendRequest
    {
        #region Public-Members

        /// <summary>
        /// Client generated UUID (required); reuse it on every retry.
        /// </summary>
        public string? ClientMessageId { get; set; } = null;

        /// <summary>
        /// Plain text, 0 to messaging.maxBodyLength characters.
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Uploaded media (purpose Message) in display order.
        /// </summary>
        public List<string>? MediaIds { get; set; } = null;

        /// <summary>
        /// Quoted message in the same conversation, or null.
        /// </summary>
        public string? ReplyToMessageId { get; set; } = null;

        /// <summary>
        /// Whether to show a link preview.
        /// </summary>
        public bool ShowLinkPreview { get; set; } = true;

        #endregion
    }
}
