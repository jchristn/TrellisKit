namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Computed state of a feed post.
    /// </summary>
    public enum FeedPostStateEnum
    {
        /// <summary>
        /// publishAtUtc is in the future.
        /// </summary>
        Scheduled,

        /// <summary>
        /// An attachment is still processing (or failed).
        /// </summary>
        Processing,

        /// <summary>
        /// Visible in the feed.
        /// </summary>
        Published,

        /// <summary>
        /// Hidden pending review by a moderator or the auto-hide rule.
        /// </summary>
        Hidden,

        /// <summary>
        /// Removed by a moderator.
        /// </summary>
        Removed,

        /// <summary>
        /// Deleted by the author or an administrator (admin views only).
        /// </summary>
        Deleted
    }
}
