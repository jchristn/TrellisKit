namespace TrellisKit.Core.Requests
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Typed conditions of the conversation list, declined list, and sync queries.
    /// </summary>
    public class ConversationListFilter
    {
        #region Public-Members

        /// <summary>
        /// Folder (Requests means a pending request to the caller), or null for every folder.
        /// </summary>
        public ConversationFolderEnum? Folder { get; set; } = null;

        /// <summary>
        /// Only conversations with unread messages by others.
        /// </summary>
        public bool UnreadOnly { get; set; } = false;

        /// <summary>
        /// Only the conversation with this member.
        /// </summary>
        public string? WithUserId { get; set; } = null;

        /// <summary>
        /// The declined requests list instead of the visible lists.
        /// </summary>
        public bool Declined { get; set; } = false;

        /// <summary>
        /// The sync query: every member row of the caller, visible or not.
        /// </summary>
        public bool Changes { get; set; } = false;

        #endregion
    }
}
