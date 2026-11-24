namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A public sample lesson (docs/PHASE4_CONTRACT.md section 5.12): the lesson summary plus description, markers,
    /// captions, and the video from the public media paths. It never carries viewer state, progress, reactions of the
    /// caller, comments, staff fields (moderation, media status, scheduling), or the uploader of the media.
    /// </summary>
    public class PublicLessonView
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
        /// Description (Markdown).
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Kind.
        /// </summary>
        public LessonKindEnum Kind { get; set; } = LessonKindEnum.Video;

        /// <summary>
        /// Duration in seconds.
        /// </summary>
        public int DurationSeconds { get; set; } = 0;

        /// <summary>
        /// Levels.
        /// </summary>
        public List<LibraryLevelView> Levels { get; set; } = new List<LibraryLevelView>();

        /// <summary>
        /// Audience (always Public).
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Public;

        /// <summary>
        /// Video from the public media paths (no uploader), or null for articles.
        /// </summary>
        public MediaView? Video { get; set; } = null;

        /// <summary>
        /// Chapter markers.
        /// </summary>
        public List<LessonMarkerView> Markers { get; set; } = new List<LessonMarkerView>();

        /// <summary>
        /// Caption tracks with public media.
        /// </summary>
        public List<LessonCaptionView> Captions { get; set; } = new List<LessonCaptionView>();

        /// <summary>
        /// Comment count (comments themselves are members only).
        /// </summary>
        public int CommentCount { get; set; } = 0;

        /// <summary>
        /// Reaction total.
        /// </summary>
        public int ReactionTotal { get; set; } = 0;

        /// <summary>
        /// Public labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Public tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// When the lesson was published.
        /// </summary>
        public DateTime? PublishedUtc { get; set; } = null;

        #endregion
    }
}
