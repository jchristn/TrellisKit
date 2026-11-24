namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A message to insert with its conversation side effects (one transaction).
    /// </summary>
    public class MessageDraft
    {
        #region Public-Members

        /// <summary>
        /// New message identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Conversation identifier.
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// Sender.
        /// </summary>
        public string SenderUserId { get; set; } = String.Empty;

        /// <summary>
        /// Idempotency key.
        /// </summary>
        public string ClientMessageId { get; set; } = String.Empty;

        /// <summary>
        /// Text.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Excerpt.
        /// </summary>
        public string Excerpt { get; set; } = String.Empty;

        /// <summary>
        /// Quoted message, or null.
        /// </summary>
        public string? ReplyToMessageId { get; set; } = null;

        /// <summary>
        /// Link preview request.
        /// </summary>
        public bool ShowLinkPreview { get; set; } = true;

        /// <summary>
        /// Link preview resolved server-side for the first link in the body (null when none, hidden, or disabled).
        /// </summary>
        public string? LinkPreviewId { get; set; } = null;

        /// <summary>
        /// Attachments in order.
        /// </summary>
        public List<string> MediaIds
        {
            get { return _MediaIds; }
            set { _MediaIds = value ?? new List<string>(); }
        }

        /// <summary>
        /// Whether an attachment is a video.
        /// </summary>
        public bool HasVideo { get; set; } = false;

        /// <summary>
        /// Messages a sender awaiting acceptance may send.
        /// </summary>
        public int RequestMessageLimit { get; set; } = 3;

        /// <summary>
        /// Current time (UTC).
        /// </summary>
        public DateTime NowUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private List<string> _MediaIds = new List<string>();

        #endregion
    }
}
