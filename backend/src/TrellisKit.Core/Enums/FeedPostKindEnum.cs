namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Kind of feed post.
    /// </summary>
    public enum FeedPostKindEnum
    {
        /// <summary>
        /// A post written for the feed.
        /// </summary>
        Original,

        /// <summary>
        /// A forum thread promoted to the feed (reserved for Phase 3).
        /// </summary>
        PromotedThread,

        /// <summary>
        /// A feed card for a lesson promoted to the feed.
        /// </summary>
        PromotedLesson,

        /// <summary>
        /// A feed card for an event promoted to the feed.
        /// </summary>
        PromotedEvent
    }
}
