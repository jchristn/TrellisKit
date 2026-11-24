namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Kind of a conversation change in the sync enumeration (docs/PHASE5_CONTRACT.md section 5.2).
    /// </summary>
    public enum ConversationChangeEnum
    {
        /// <summary>
        /// The conversation changed and is visible to the caller.
        /// </summary>
        Updated,

        /// <summary>
        /// The conversation left the caller's lists (deleted for the caller, declined, or the other member was blocked).
        /// </summary>
        Removed
    }
}
