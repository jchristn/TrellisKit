namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A link from a lesson to a catalog entry it teaches (table lessoncatalogentries, prefix lce_): "Techniques in this
    /// lesson" and "Lessons that teach this". Unique per lesson and entry; at most <see cref="MaxPerLesson"/> per lesson.
    /// Labels and tags live in the shared labels and tags tables (target type LessonCatalogEntry).
    /// </summary>
    public class LessonCatalogEntry
    {
        #region Public-Members

        /// <summary>
        /// Most links one lesson may carry.
        /// </summary>
        public const int MaxPerLesson = 50;

        /// <summary>
        /// Largest start offset in seconds (24 hours).
        /// </summary>
        public const int MaxStartSeconds = 86400;

        /// <summary>
        /// Identifier (lce_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Lesson identifier.
        /// </summary>
        public string LessonId { get; set; } = String.Empty;

        /// <summary>
        /// Catalog entry identifier (cex_).
        /// </summary>
        public string CatalogEntryId { get; set; } = String.Empty;

        /// <summary>
        /// Order inside the lesson, ascending from 1. Minimum 1, maximum <see cref="MaxPerLesson"/>.
        /// </summary>
        public int Position { get; set; } = 1;

        /// <summary>
        /// Where in the lesson video the technique appears (0 to <see cref="MaxStartSeconds"/> seconds), or null.
        /// </summary>
        public int? StartSeconds { get; set; } = null;

        /// <summary>
        /// How the link was created. Default Editor.
        /// </summary>
        public LessonCatalogLinkSourceEnum Source { get; set; } = LessonCatalogLinkSourceEnum.Editor;

        /// <summary>
        /// Member who created the link.
        /// </summary>
        public string CreatedBy { get; set; } = String.Empty;

        /// <summary>
        /// Labels (shared labels table).
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags (shared tags table).
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// UTC creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
