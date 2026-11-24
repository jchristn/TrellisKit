namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// A member's folder for a conversation (docs/PHASE5_CONTRACT.md section 2.1).
    /// </summary>
    public enum ConversationFolderEnum
    {
        /// <summary>
        /// The main list.
        /// </summary>
        Inbox,

        /// <summary>
        /// A pending request sent to the member.
        /// </summary>
        Requests,

        /// <summary>
        /// Archived by the member.
        /// </summary>
        Archived
    }
}
