namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A direct message row (docs/PHASE5_CONTRACT.md section 16 and as-built note B).
    /// </summary>
    public class Message
    {
        #region Public-Members

        /// <summary>
        /// Identifier (msg_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Conversation identifier.
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// Sequence number in the conversation.
        /// </summary>
        public long Seq { get; set; } = 0;

        /// <summary>
        /// Client generated idempotency key.
        /// </summary>
        public string ClientMessageId { get; set; } = String.Empty;

        /// <summary>
        /// Author, or null after the author's account was deleted.
        /// </summary>
        public string? AuthorUserId { get; set; } = null;

        /// <summary>
        /// Text (kept internally after a delete for everyone; purged on account deletion).
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Text before the one allowed edit, or null.
        /// </summary>
        public string? OriginalBody { get; set; } = null;

        /// <summary>
        /// First 120 characters of the body.
        /// </summary>
        public string Excerpt { get; set; } = String.Empty;

        /// <summary>
        /// Quoted message, or null.
        /// </summary>
        public string? ReplyToMessageId { get; set; } = null;

        /// <summary>
        /// Link preview, or null.
        /// </summary>
        public string? LinkPreviewId { get; set; } = null;

        /// <summary>
        /// Whether the author asked for a link preview.
        /// </summary>
        public bool ShowLinkPreview { get; set; } = true;

        /// <summary>
        /// Number of attachments.
        /// </summary>
        public int AttachmentCount { get; set; } = 0;

        /// <summary>
        /// Whether an attachment is a video.
        /// </summary>
        public bool HasVideo { get; set; } = false;

        /// <summary>
        /// State.
        /// </summary>
        public MessageStateEnum State { get; set; } = MessageStateEnum.Visible;

        /// <summary>
        /// Edited (UTC), or null.
        /// </summary>
        public DateTime? EditedUtc { get; set; } = null;

        /// <summary>
        /// Deleted for everyone (UTC), or null.
        /// </summary>
        public DateTime? DeletedUtc { get; set; } = null;

        /// <summary>
        /// Removed or restored by a moderator (UTC), or null.
        /// </summary>
        public DateTime? ModeratedUtc { get; set; } = null;

        /// <summary>
        /// Moderator, or null.
        /// </summary>
        public string? ModeratedByUserId { get; set; } = null;

        /// <summary>
        /// Reaction count.
        /// </summary>
        public int ReactionTotal { get; set; } = 0;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Attached media in display order (loaded from messageattachments).
        /// </summary>
        public List<string> MediaIds
        {
            get { return _MediaIds; }
            set { _MediaIds = value ?? new List<string>(); }
        }

        #endregion

        #region Private-Members

        private List<string> _MediaIds = new List<string>();

        #endregion
    }
}
