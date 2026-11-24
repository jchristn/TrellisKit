namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A library lesson (lessons) with its child lists.
    /// </summary>
    public class Lesson
    {
        #region Public-Members

        /// <summary>
        /// Identifier (les_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Series (one series per lesson).
        /// </summary>
        public string SeriesId { get; set; } = String.Empty;

        /// <summary>
        /// Position inside the series (1-based).
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Slug, unique per series.
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Summary, or null.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Description (Markdown subset; articles in full), or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Kind.
        /// </summary>
        public LessonKindEnum Kind { get; set; } = LessonKindEnum.Video;

        /// <summary>
        /// Video media, or null.
        /// </summary>
        public string? VideoMediaId { get; set; } = null;

        /// <summary>
        /// Duration in seconds (from the media).
        /// </summary>
        public int DurationSeconds { get; set; } = 0;

        /// <summary>
        /// Status.
        /// </summary>
        public LibraryStatusEnum Status { get; set; } = LibraryStatusEnum.Draft;

        /// <summary>
        /// Scheduled publication time, or null.
        /// </summary>
        public DateTime? PublishAtUtc { get; set; } = null;

        /// <summary>
        /// Audience (may be broader than the series).
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// Staff switch for offline downloads.
        /// </summary>
        public bool Downloadable { get; set; } = true;

        /// <summary>
        /// Whether members may comment.
        /// </summary>
        public bool CommentsEnabled { get; set; } = true;

        /// <summary>
        /// Whether library staff receive PostComment for new comments.
        /// </summary>
        public bool NotifyStaffOnComment { get; set; } = false;

        /// <summary>
        /// Visible comments.
        /// </summary>
        public int CommentCount { get; set; } = 0;

        /// <summary>
        /// Reactions on the lesson.
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
        /// Last moderation change.
        /// </summary>
        public DateTime? ModeratedUtc { get; set; } = null;

        /// <summary>
        /// Moderator of the last change.
        /// </summary>
        public string? ModeratedByUserId { get; set; } = null;

        /// <summary>
        /// Reason of the last change.
        /// </summary>
        public string? ModerationReason { get; set; } = null;

        /// <summary>
        /// Feed card of an active promotion, or null.
        /// </summary>
        public string? PromotedFeedPostId { get; set; } = null;

        /// <summary>
        /// First publication.
        /// </summary>
        public DateTime? PublishedUtc { get; set; } = null;

        /// <summary>
        /// When archived, or null.
        /// </summary>
        public DateTime? ArchivedUtc { get; set; } = null;

        /// <summary>
        /// When deleted, or null.
        /// </summary>
        public DateTime? DeletedUtc { get; set; } = null;

        /// <summary>
        /// Creator, or null.
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last change.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Level keys (at most 10).
        /// </summary>
        public List<string> Levels { get; set; } = new List<string>();

        /// <summary>
        /// Markers sorted by seconds.
        /// </summary>
        public List<LessonMarker> Markers { get; set; } = new List<LessonMarker>();

        /// <summary>
        /// Resources in order.
        /// </summary>
        public List<LessonResource> Resources { get; set; } = new List<LessonResource>();

        /// <summary>
        /// Caption tracks in order.
        /// </summary>
        public List<LessonCaption> Captions { get; set; } = new List<LessonCaption>();

        #endregion
    }
}
