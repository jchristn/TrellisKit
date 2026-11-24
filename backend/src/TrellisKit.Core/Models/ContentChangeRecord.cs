namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One content changelog row (docs/INTEGRATION.md content review contract): a create, edit, or delete of a member
    /// contribution, written by a database trigger in the same transaction as the change, and its Terms of Service
    /// review. Not thread safe.
    /// </summary>
    public class ContentChangeRecord
    {
        #region Public-Members

        /// <summary>
        /// Identifier (chg_...).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Type of the changed contribution (a <see cref="TargetTypeEnum"/> name such as FeedPost, Comment, Thread,
        /// Message, or User, or VenueReviewReply for the reply on a venue review).
        /// </summary>
        public string TargetType { get; set; } = String.Empty;

        /// <summary>
        /// Identifier of the changed contribution.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Type of the containing object (for example the FeedPost of a comment, the Conversation of a message, or the
        /// ProfileField of a profile value), or null.
        /// </summary>
        public string? ContextType { get; set; } = null;

        /// <summary>
        /// Identifier of the containing object, or null.
        /// </summary>
        public string? ContextId { get; set; } = null;

        /// <summary>
        /// What happened.
        /// </summary>
        public ContentChangeActionEnum Action { get; set; } = ContentChangeActionEnum.Create;

        /// <summary>
        /// Member who made the change (the signed-in caller, else the contribution's author), or null.
        /// </summary>
        public string? ActorUserId { get; set; } = null;

        /// <summary>
        /// Author or owner of the contribution, or null.
        /// </summary>
        public string? AuthorUserId { get; set; } = null;

        /// <summary>
        /// Client the change came from.
        /// </summary>
        public ContentChangeClientEnum Client { get; set; } = ContentChangeClientEnum.System;

        /// <summary>
        /// Text of the contribution after the change ("column: value" lines; at most 20000 characters). Empty for
        /// deletions, whose text is in the row of the version that was deleted.
        /// </summary>
        public string Snapshot { get; set; } = String.Empty;

        /// <summary>
        /// Media the contribution references (media identifiers or avatar files). Never null.
        /// </summary>
        public List<string> MediaRefs
        {
            get { return _MediaRefs; }
            set { _MediaRefs = value ?? new List<string>(); }
        }

        /// <summary>
        /// Review state.
        /// </summary>
        public ContentReviewStatusEnum ReviewStatus { get; set; } = ContentReviewStatusEnum.Pending;

        /// <summary>
        /// When it was reviewed, or null.
        /// </summary>
        public DateTime? ReviewedUtc { get; set; } = null;

        /// <summary>
        /// The reviewer: "model:&lt;model&gt;", "user:&lt;id&gt;", "admin-token", or "system"; null while Pending.
        /// </summary>
        public string? ReviewedBy { get; set; } = null;

        /// <summary>
        /// Kind of reviewer, or null while Pending.
        /// </summary>
        public ContentReviewerKindEnum? ReviewerKind { get; set; } = null;

        /// <summary>
        /// Categories the reviewer reported. Never null; at most one of each category.
        /// </summary>
        public List<ContentReviewCategoryEnum> Categories
        {
            get { return _Categories; }
            set { _Categories = value ?? new List<ContentReviewCategoryEnum>(); }
        }

        /// <summary>
        /// Severity the reviewer reported.
        /// </summary>
        public ContentReviewSeverityEnum Severity { get; set; } = ContentReviewSeverityEnum.None;

        /// <summary>
        /// The reviewer's note (model reason or administrator note), or null.
        /// </summary>
        public string? ReviewerNote { get; set; } = null;

        /// <summary>
        /// Failed review attempts so far.
        /// </summary>
        public int ReviewAttempts { get; set; } = 0;

        /// <summary>
        /// The last review failure, or null.
        /// </summary>
        public string? LastError { get; set; } = null;

        /// <summary>
        /// When administrators were notified about the flag, or null.
        /// </summary>
        public DateTime? NotifiedUtc { get; set; } = null;

        /// <summary>
        /// The moderation action an administrator took from the queue, or null.
        /// </summary>
        public string? ModerationActionId { get; set; } = null;

        /// <summary>
        /// When the change happened (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update of the row (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private List<string> _MediaRefs = new List<string>();
        private List<ContentReviewCategoryEnum> _Categories = new List<ContentReviewCategoryEnum>();

        #endregion
    }
}
