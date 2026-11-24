namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A library series: a course of ordered lessons (series).
    /// </summary>
    public class LibrarySeries
    {
        #region Public-Members

        /// <summary>
        /// Identifier (ser_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Slug, unique per property.
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
        /// Description (Markdown subset), or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Cover image media, or null.
        /// </summary>
        public string? CoverMediaId { get; set; } = null;

        /// <summary>
        /// Status (Draft, Published, Archived).
        /// </summary>
        public LibraryStatusEnum Status { get; set; } = LibraryStatusEnum.Draft;

        /// <summary>
        /// Sort order.
        /// </summary>
        public int SortOrder { get; set; } = 0;

        /// <summary>
        /// Audience; the default audience of new lessons.
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// Published, unarchived lessons (recomputed in the same transaction as every lesson write).
        /// </summary>
        public int LessonCount { get; set; } = 0;

        /// <summary>
        /// Sum of the durations of those lessons.
        /// </summary>
        public int TotalDurationSeconds { get; set; } = 0;

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

        #endregion
    }
}
