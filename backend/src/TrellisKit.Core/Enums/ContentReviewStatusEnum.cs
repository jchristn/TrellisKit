namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Review state of one content changelog row.
    /// </summary>
    public enum ContentReviewStatusEnum
    {
        /// <summary>
        /// Waiting for the reviewer.
        /// </summary>
        Pending,

        /// <summary>
        /// Reviewed; nothing violates the Terms of Service.
        /// </summary>
        Clean,

        /// <summary>
        /// Reviewed; a possible violation waits for an administrator.
        /// </summary>
        Flagged,

        /// <summary>
        /// An administrator found nothing to act on.
        /// </summary>
        Dismissed,

        /// <summary>
        /// An administrator acted (hide, remove, warn, mute, or ban).
        /// </summary>
        Actioned
    }
}
