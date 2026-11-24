namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A direct message (docs/PHASE5_CONTRACT.md section 2.2 and as-built note B).
    /// </summary>
    public class DirectMessageView
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
        /// Sequence number (contiguous per conversation).
        /// </summary>
        public long Seq { get; set; } = 0;

        /// <summary>
        /// The client idempotency key (returned to the author only).
        /// </summary>
        public string? ClientMessageId { get; set; } = null;

        /// <summary>
        /// Author ("Deleted member" after account deletion).
        /// </summary>
        public UserSummary Author { get; set; } = new UserSummary();

        /// <summary>
        /// Text; empty for deleted and removed messages.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Text before the one edit (edit history), null when not edited, deleted, or removed.
        /// </summary>
        public string? OriginalBody { get; set; } = null;

        /// <summary>
        /// Whether the message was edited.
        /// </summary>
        public bool IsEdited { get; set; } = false;

        /// <summary>
        /// Edited (UTC), or null.
        /// </summary>
        public DateTime? EditedUtc { get; set; } = null;

        /// <summary>
        /// Attachments (bounded by messaging.maxAttachments); empty for deleted and removed messages.
        /// </summary>
        public List<MessageAttachmentView> Attachments
        {
            get { return _Attachments; }
            set { _Attachments = value ?? new List<MessageAttachmentView>(); }
        }

        /// <summary>
        /// Link preview, or null.
        /// </summary>
        public object? LinkPreview { get; set; } = null;

        /// <summary>
        /// Quoted message, or null.
        /// </summary>
        public MessageReplyView? ReplyTo { get; set; } = null;

        /// <summary>
        /// State.
        /// </summary>
        public MessageStateEnum State { get; set; } = MessageStateEnum.Visible;

        /// <summary>
        /// Reaction counts by key.
        /// </summary>
        public Dictionary<string, long> ReactionCounts
        {
            get { return _ReactionCounts; }
            set { _ReactionCounts = value ?? new Dictionary<string, long>(); }
        }

        /// <summary>
        /// Reaction total.
        /// </summary>
        public long ReactionTotal { get; set; } = 0;

        /// <summary>
        /// The caller's reaction keys.
        /// </summary>
        public List<string> MyReactions
        {
            get { return _MyReactions; }
            set { _MyReactions = value ?? new List<string>(); }
        }

        /// <summary>
        /// The author's private labels (empty for other viewers).
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// The author's private tags (empty for other viewers).
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        /// <summary>
        /// Capabilities.
        /// </summary>
        public MessageViewer Viewer { get; set; } = new MessageViewer();

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private List<MessageAttachmentView> _Attachments = new List<MessageAttachmentView>();
        private Dictionary<string, long> _ReactionCounts = new Dictionary<string, long>();
        private List<string> _MyReactions = new List<string>();
        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
