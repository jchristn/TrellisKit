namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Preference category of a notification.
    /// </summary>
    public enum NotificationCategoryEnum
    {
        /// <summary>
        /// Comments on your posts and replies to your comments.
        /// </summary>
        Replies,

        /// <summary>
        /// Mentions of you.
        /// </summary>
        Mentions,

        /// <summary>
        /// Reactions to your content.
        /// </summary>
        Reactions,

        /// <summary>
        /// New feed posts.
        /// </summary>
        FeedPosts,

        /// <summary>
        /// Moderation notices.
        /// </summary>
        Moderation,

        /// <summary>
        /// Account and membership notices.
        /// </summary>
        Account,

        /// <summary>
        /// Activity on followed threads.
        /// </summary>
        Following,

        /// <summary>
        /// Forum events: new threads in followed forums, accepted answers, reviews, polls, promotions.
        /// </summary>
        Forums,

        /// <summary>
        /// Event reminders, changes, and cancellations.
        /// </summary>
        Events,

        /// <summary>
        /// New lessons in followed series.
        /// </summary>
        Library,

        /// <summary>
        /// Direct messages and message requests.
        /// </summary>
        Messages,

        /// <summary>
        /// Ideas board status changes.
        /// </summary>
        Ideas,

        /// <summary>
        /// Staff broadcasts.
        /// </summary>
        Announcements,

        /// <summary>
        /// Support ticket activity (support staff only).
        /// </summary>
        Support
    }
}
