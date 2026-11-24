namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// The target of a notification.
    /// </summary>
    public class NotificationTarget
    {
        /// <summary>
        /// Target type (FeedPost, Comment, User, or a type from a later phase).
        /// </summary>
        public string Type { get; set; } = String.Empty;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;
    }
}
