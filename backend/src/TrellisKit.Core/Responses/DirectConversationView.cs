namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A conversation as the caller sees it (docs/PHASE5_CONTRACT.md section 2.1).
    /// </summary>
    public class DirectConversationView
    {
        #region Public-Members

        /// <summary>
        /// Identifier (cnv_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Kind.
        /// </summary>
        public ConversationKindEnum Kind { get; set; } = ConversationKindEnum.Direct;

        /// <summary>
        /// Members (bounded: 2 for Direct).
        /// </summary>
        public List<ConversationMemberView> Members
        {
            get { return _Members; }
            set { _Members = value ?? new List<ConversationMemberView>(); }
        }

        /// <summary>
        /// The member who is not the caller.
        /// </summary>
        public UserSummary? OtherMember { get; set; } = null;

        /// <summary>
        /// The caller's folder.
        /// </summary>
        public ConversationFolderEnum Folder { get; set; } = ConversationFolderEnum.Inbox;

        /// <summary>
        /// Pending (recipient), AwaitingAcceptance (sender), or null.
        /// </summary>
        public ConversationRequestStateEnum? RequestState { get; set; } = null;

        /// <summary>
        /// Latest message visible to the caller, or null.
        /// </summary>
        public MessageSummaryView? LastMessage { get; set; } = null;

        /// <summary>
        /// Highest sequence number.
        /// </summary>
        public long LastSeq { get; set; } = 0;

        /// <summary>
        /// The caller's read position.
        /// </summary>
        public long MyLastReadSeq { get; set; } = 0;

        /// <summary>
        /// The caller's delivery position.
        /// </summary>
        public long MyLastDeliveredSeq { get; set; } = 0;

        /// <summary>
        /// The other member's read position, null when either member turned read receipts off or during a request.
        /// </summary>
        public long? OtherLastReadSeq { get; set; } = null;

        /// <summary>
        /// Messages by others above the caller's read position (capped at 999).
        /// </summary>
        public int UnreadCount { get; set; } = 0;

        /// <summary>
        /// Whether the caller muted the conversation.
        /// </summary>
        public bool IsMuted { get; set; } = false;

        /// <summary>
        /// Mute end, or null.
        /// </summary>
        public DateTime? MutedUntilUtc { get; set; } = null;

        /// <summary>
        /// The caller's private labels (metadata target ConversationMember, the cvm_ id).
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// The caller's private tags.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        /// <summary>
        /// The caller's member row identifier (cvm_), the metadata target of labels and tags.
        /// </summary>
        public string MemberId { get; set; } = String.Empty;

        /// <summary>
        /// Capabilities.
        /// </summary>
        public ConversationViewer Viewer { get; set; } = new ConversationViewer();

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

        private List<ConversationMemberView> _Members = new List<ConversationMemberView>();
        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
