namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Fields shared by lesson list items and lesson details.
    /// </summary>
    public class LessonBaseView
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Series.
        /// </summary>
        public SeriesRef Series { get; set; } = new SeriesRef();

        /// <summary>
        /// Position in the series.
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Slug.
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Summary.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Kind.
        /// </summary>
        public LessonKindEnum Kind { get; set; } = LessonKindEnum.Video;

        /// <summary>
        /// Duration.
        /// </summary>
        public int DurationSeconds { get; set; } = 0;

        /// <summary>
        /// Whether the lesson has resources.
        /// </summary>
        public bool HasResources { get; set; } = false;

        /// <summary>
        /// Levels.
        /// </summary>
        public List<LibraryLevelView> Levels { get; set; } = new List<LibraryLevelView>();

        /// <summary>
        /// Status.
        /// </summary>
        public LibraryStatusEnum Status { get; set; } = LibraryStatusEnum.Published;

        /// <summary>
        /// Scheduled publication time.
        /// </summary>
        public DateTime? PublishAtUtc { get; set; } = null;

        /// <summary>
        /// Audience.
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// Staff download switch.
        /// </summary>
        public bool Downloadable { get; set; } = true;

        /// <summary>
        /// Comments on.
        /// </summary>
        public bool CommentsEnabled { get; set; } = true;

        /// <summary>
        /// Comments.
        /// </summary>
        public int CommentCount { get; set; } = 0;

        /// <summary>
        /// Reactions.
        /// </summary>
        public int ReactionTotal { get; set; } = 0;

        /// <summary>
        /// The caller's progress, or null.
        /// </summary>
        public LessonProgressView? MyProgress { get; set; } = null;

        /// <summary>
        /// Locked stub.
        /// </summary>
        public bool Locked { get; set; } = false;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Viewer capabilities.
        /// </summary>
        public LessonViewer Viewer { get; set; } = new LessonViewer();

        /// <summary>
        /// First publication.
        /// </summary>
        public DateTime? PublishedUtc { get; set; } = null;

        /// <summary>
        /// When archived.
        /// </summary>
        public DateTime? ArchivedUtc { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last change.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
