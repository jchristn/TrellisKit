namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One row of the content review queue (GET /api/v1/admin/content-review/{id} and the enumeration). Lists are
    /// bounded: categories at most the 8 categories, mediaRefs at most the media of one contribution, labels and tags 50
    /// each.
    /// </summary>
    public class ContentChangeView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (chg_...).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Type of the changed contribution (FeedPost, Comment, Thread, PollOption, Message, VenueReview,
        /// VenueReviewReply, User, Media, DirectorySubmission, VenueMedia, Event, Lesson, Idea, Ticket, TicketMessage,
        /// AssistantMessage).
        /// </summary>
        public string TargetType { get; set; } = String.Empty;

        /// <summary>
        /// Identifier of the changed contribution.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Type of the containing object, or null.
        /// </summary>
        public string? ContextType { get; set; } = null;

        /// <summary>
        /// Identifier of the containing object, or null.
        /// </summary>
        public string? ContextId { get; set; } = null;

        /// <summary>
        /// Create, Update, or Delete.
        /// </summary>
        public string Action { get; set; } = "Create";

        /// <summary>
        /// Member who made the change, or null.
        /// </summary>
        public UserSummary? Actor { get; set; } = null;

        /// <summary>
        /// Author or owner of the contribution, or null.
        /// </summary>
        public UserSummary? Author { get; set; } = null;

        /// <summary>
        /// Web, Mobile, Api, or System.
        /// </summary>
        public string Client { get; set; } = "System";

        /// <summary>
        /// Text after the change.
        /// </summary>
        public string Snapshot { get; set; } = String.Empty;

        /// <summary>
        /// Referenced media (identifiers or avatar files).
        /// </summary>
        public List<string> MediaRefs { get; set; } = new List<string>();

        /// <summary>
        /// Pending, Clean, Flagged, Dismissed, or Actioned.
        /// </summary>
        public string ReviewStatus { get; set; } = "Pending";

        /// <summary>
        /// When it was reviewed, or null.
        /// </summary>
        public DateTime? ReviewedUtc { get; set; } = null;

        /// <summary>
        /// The reviewer ("model:...", "user:...", "admin-token", "system"), or null.
        /// </summary>
        public string? ReviewedBy { get; set; } = null;

        /// <summary>
        /// Model, Administrator, or System; null while Pending.
        /// </summary>
        public string? ReviewerKind { get; set; } = null;

        /// <summary>
        /// Reported categories.
        /// </summary>
        public List<string> Categories { get; set; } = new List<string>();

        /// <summary>
        /// None, Low, Medium, High, or Critical.
        /// </summary>
        public string Severity { get; set; } = "None";

        /// <summary>
        /// Reviewer note, or null.
        /// </summary>
        public string? ReviewerNote { get; set; } = null;

        /// <summary>
        /// Failed review attempts.
        /// </summary>
        public int ReviewAttempts { get; set; } = 0;

        /// <summary>
        /// Last review failure, or null.
        /// </summary>
        public string? LastError { get; set; } = null;

        /// <summary>
        /// When administrators were notified, or null.
        /// </summary>
        public DateTime? NotifiedUtc { get; set; } = null;

        /// <summary>
        /// Moderation action taken from the queue, or null.
        /// </summary>
        public string? ModerationActionId { get; set; } = null;

        /// <summary>
        /// The moderation target type that Hide and Remove act on, or null when the content has no hide and remove
        /// moderation (Warn, Mute, and Ban still apply when there is an author).
        /// </summary>
        public string? ModerationTargetType { get; set; } = null;

        /// <summary>
        /// When the change happened.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update of the row.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <inheritdoc />
        public List<string> Labels { get; set; } = new List<string>();

        /// <inheritdoc />
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
