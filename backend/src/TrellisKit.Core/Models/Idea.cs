namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An ideas board row (docs/PHASE5_CONTRACT.md sections 2.4 and 16).
    /// </summary>
    public class Idea
    {
        #region Public-Members

        /// <summary>
        /// Identifier (ida_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Body (Markdown subset).
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Author, or null (deleted member, or a staff idea without an author).
        /// </summary>
        public string? AuthorUserId { get; set; } = null;

        /// <summary>
        /// Whether the author is shown (false hides a ticket requester).
        /// </summary>
        public bool ShowAuthor { get; set; } = true;

        /// <summary>
        /// Status key.
        /// </summary>
        public string StatusKey { get; set; } = String.Empty;

        /// <summary>
        /// Staff note for the current status, or null.
        /// </summary>
        public string? StatusNote { get; set; } = null;

        /// <summary>
        /// When the status last changed, or null.
        /// </summary>
        public DateTime? StatusUtc { get; set; } = null;

        /// <summary>
        /// Votes (recomputed from ideavotes).
        /// </summary>
        public int VoteCount { get; set; } = 0;

        /// <summary>
        /// Votes in the trending window (recomputed by the trending worker).
        /// </summary>
        public int RecentVoteCount { get; set; } = 0;

        /// <summary>
        /// The idea this one was merged into, or null.
        /// </summary>
        public string? MergedIntoId { get; set; } = null;

        /// <summary>
        /// The ticket this idea was published from, or null.
        /// </summary>
        public string? SourceTicketId { get; set; } = null;

        /// <summary>
        /// Audience.
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// Visible comments.
        /// </summary>
        public int CommentCount { get; set; } = 0;

        /// <summary>
        /// Reactions.
        /// </summary>
        public int ReactionTotal { get; set; } = 0;

        /// <summary>
        /// Open reports.
        /// </summary>
        public int OpenReportCount { get; set; } = 0;

        /// <summary>
        /// Moderation state.
        /// </summary>
        public ModerationStateEnum ModerationState { get; set; } = ModerationStateEnum.None;

        /// <summary>
        /// When moderated, or null.
        /// </summary>
        public DateTime? ModeratedUtc { get; set; } = null;

        /// <summary>
        /// Moderator, or null (system).
        /// </summary>
        public string? ModeratedByUserId { get; set; } = null;

        /// <summary>
        /// Moderation reason, or null.
        /// </summary>
        public string? ModerationReason { get; set; } = null;

        /// <summary>
        /// Staff member who created the idea, or null (member submission).
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// Last edit, or null.
        /// </summary>
        public DateTime? EditedUtc { get; set; } = null;

        /// <summary>
        /// Soft delete time, or null.
        /// </summary>
        public DateTime? DeletedUtc { get; set; } = null;

        /// <summary>
        /// When the content purge worker cleared the idea after moderation.retainRemovedDays, or null.
        /// </summary>
        public DateTime? PurgedUtc { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
