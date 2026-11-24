namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Kind of a direct messaging conversation (docs/PHASE5_CONTRACT.md section 2.1).
    /// </summary>
    public enum ConversationKindEnum
    {
        /// <summary>
        /// Exactly two members.
        /// </summary>
        Direct,

        /// <summary>
        /// Reserved for group conversations (not enabled in Phase 5).
        /// </summary>
        Group
    }
}
