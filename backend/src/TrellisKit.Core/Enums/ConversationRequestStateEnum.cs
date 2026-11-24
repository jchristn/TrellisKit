namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Message request state of one member's side of a conversation (docs/PHASE5_CONTRACT.md section 3.2).
    /// </summary>
    public enum ConversationRequestStateEnum
    {
        /// <summary>
        /// The member received a request and has not accepted, declined, or replied.
        /// </summary>
        Pending,

        /// <summary>
        /// The member sent a request the other member has not accepted yet.
        /// </summary>
        AwaitingAcceptance,

        /// <summary>
        /// The member declined the request (hidden from the member's lists; undo by accepting).
        /// </summary>
        Declined
    }
}
