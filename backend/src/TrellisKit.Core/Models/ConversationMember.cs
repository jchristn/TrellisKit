namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One member's state in a conversation (docs/PHASE5_CONTRACT.md section 16).
    /// </summary>
    public class ConversationMember
    {
        #region Public-Members

        /// <summary>
        /// Identifier (cvm_); the metadata target of the member's private labels and tags.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Conversation identifier.
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// Member identifier.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Role.
        /// </summary>
        public ConversationMemberRoleEnum Role { get; set; } = ConversationMemberRoleEnum.Member;

        /// <summary>
        /// Folder.
        /// </summary>
        public ConversationFolderEnum Folder { get; set; } = ConversationFolderEnum.Inbox;

        /// <summary>
        /// Request state, or null once accepted or for known contacts.
        /// </summary>
        public ConversationRequestStateEnum? RequestState { get; set; } = null;

        /// <summary>
        /// Messages the member sent while awaiting acceptance.
        /// </summary>
        public int RequestMessageCount { get; set; } = 0;

        /// <summary>
        /// When the member accepted a request (UTC), or null.
        /// </summary>
        public DateTime? AcceptedUtc { get; set; } = null;

        /// <summary>
        /// When the member declined a request (UTC), or null.
        /// </summary>
        public DateTime? DeclinedUtc { get; set; } = null;

        /// <summary>
        /// Read position (monotonic).
        /// </summary>
        public long LastReadSeq { get; set; } = 0;

        /// <summary>
        /// Delivery position (monotonic).
        /// </summary>
        public long LastDeliveredSeq { get; set; } = 0;

        /// <summary>
        /// Messages with seq at or below this are hidden for the member (delete for me).
        /// </summary>
        public long HiddenBeforeSeq { get; set; } = 0;

        /// <summary>
        /// Whether notifications are muted.
        /// </summary>
        public bool IsMuted { get; set; } = false;

        /// <summary>
        /// Mute end (UTC), or null for indefinitely.
        /// </summary>
        public DateTime? MutedUntilUtc { get; set; } = null;

        /// <summary>
        /// Last activity visible to the member (inbox ordering).
        /// </summary>
        public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Joined (UTC).
        /// </summary>
        public DateTime JoinedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Left (groups), or null.
        /// </summary>
        public DateTime? LeftUtc { get; set; } = null;

        /// <summary>
        /// Deleted for the member (UTC), or null.
        /// </summary>
        public DateTime? DeletedUtc { get; set; } = null;

        /// <summary>
        /// Last change of the member's state (sync ordering).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
