namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One message of a member's account export (docs/PHASE5_CONTRACT.md section 12).
    /// </summary>
    public class MessageExportRow
    {
        #region Public-Members

        /// <summary>
        /// Message identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Conversation identifier.
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// Sequence number.
        /// </summary>
        public long Seq { get; set; } = 0;

        /// <summary>
        /// Author, or null.
        /// </summary>
        public string? AuthorUserId { get; set; } = null;

        /// <summary>
        /// Whether the exporting member wrote it.
        /// </summary>
        public bool IsMine { get; set; } = false;

        /// <summary>
        /// Text (empty for tombstones and removed messages).
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Text before the edit, or null.
        /// </summary>
        public string? OriginalBody { get; set; } = null;

        /// <summary>
        /// State.
        /// </summary>
        public MessageStateEnum State { get; set; } = MessageStateEnum.Visible;

        /// <summary>
        /// Attachments.
        /// </summary>
        public int AttachmentCount { get; set; } = 0;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Edited (UTC), or null.
        /// </summary>
        public DateTime? EditedUtc { get; set; } = null;

        #endregion
    }
}
