namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Why the viewer cannot send in a conversation (docs/PHASE5_CONTRACT.md section 1.4).
    /// </summary>
    public enum MessagingCannotSendReasonEnum
    {
        /// <summary>
        /// The viewer blocked the other member (shown only to the member who blocked).
        /// </summary>
        Blocked,

        /// <summary>
        /// The other member lost messaging access.
        /// </summary>
        RecipientUnavailable,

        /// <summary>
        /// The viewer reached the message limit of a pending request.
        /// </summary>
        RequestPending,

        /// <summary>
        /// The viewer is muted or may not send direct messages.
        /// </summary>
        MessagingRestricted,

        /// <summary>
        /// Sending is not possible (used for every reason that must not be revealed, such as a block by the other member).
        /// </summary>
        Unavailable
    }
}
