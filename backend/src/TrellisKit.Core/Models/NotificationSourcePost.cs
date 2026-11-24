namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The columns of a feedposts row that notification fan-out needs, read directly by the notification data access.
    /// </summary>
    public class NotificationSourcePost
    {
        /// <summary>
        /// Feed post identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Author.
        /// </summary>
        public string AuthorUserId { get; set; } = String.Empty;

        /// <summary>
        /// Title, or null.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Plain-text excerpt.
        /// </summary>
        public string Excerpt { get; set; } = String.Empty;

        /// <summary>
        /// When it was published, or null while unpublished.
        /// </summary>
        public DateTime? PublishedUtc { get; set; } = null;

        /// <summary>
        /// When it was deleted, or null.
        /// </summary>
        public DateTime? DeletedUtc { get; set; } = null;

        /// <summary>
        /// Moderation state.
        /// </summary>
        public ModerationStateEnum ModerationState { get; set; } = ModerationStateEnum.None;

        /// <summary>
        /// Whether the post may be fanned out: published, not deleted, and not moderated.
        /// </summary>
        public bool IsFanoutable
        {
            get { return PublishedUtc != null && DeletedUtc == null && ModerationState == ModerationStateEnum.None; }
        }
    }
}
