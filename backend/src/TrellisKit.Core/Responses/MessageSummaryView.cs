namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Conversation list preview of the latest message (docs/PHASE5_CONTRACT.md section 2.2).
    /// </summary>
    public class MessageSummaryView
    {
        #region Public-Members

        /// <summary>
        /// Message identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Sequence number.
        /// </summary>
        public long Seq { get; set; } = 0;

        /// <summary>
        /// Author, or null.
        /// </summary>
        public string? AuthorUserId { get; set; } = null;

        /// <summary>
        /// At most 120 characters; empty for deleted and removed messages.
        /// </summary>
        public string Excerpt { get; set; } = String.Empty;

        /// <summary>
        /// Whether the message has attachments.
        /// </summary>
        public bool HasAttachments { get; set; } = false;

        /// <summary>
        /// State.
        /// </summary>
        public MessageStateEnum State { get; set; } = MessageStateEnum.Visible;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
