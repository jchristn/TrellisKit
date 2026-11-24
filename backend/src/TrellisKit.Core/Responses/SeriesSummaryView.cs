namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A series list item (SeriesSummary).
    /// </summary>
    public class SeriesSummaryView
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

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
        /// Cover image, or null.
        /// </summary>
        public MediaView? Cover { get; set; } = null;

        /// <summary>
        /// Levels.
        /// </summary>
        public List<LibraryLevelView> Levels { get; set; } = new List<LibraryLevelView>();

        /// <summary>
        /// Status.
        /// </summary>
        public LibraryStatusEnum Status { get; set; } = LibraryStatusEnum.Published;

        /// <summary>
        /// Sort order.
        /// </summary>
        public int SortOrder { get; set; } = 0;

        /// <summary>
        /// Published lessons.
        /// </summary>
        public int LessonCount { get; set; } = 0;

        /// <summary>
        /// Total duration of the published lessons.
        /// </summary>
        public int TotalDurationSeconds { get; set; } = 0;

        /// <summary>
        /// The caller's progress, or null.
        /// </summary>
        public SeriesProgressView? MyProgress { get; set; } = null;

        /// <summary>
        /// Whether the caller follows it.
        /// </summary>
        public bool IsFollowed { get; set; } = false;

        /// <summary>
        /// Audience.
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

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
        public SeriesViewer Viewer { get; set; } = new SeriesViewer();

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
