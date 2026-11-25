namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One "Techniques in this lesson" item: the caller's view of a linked catalog entry plus the link's own fields
    /// (position, start offset, source). Library staff also see links to unpublished entries, with
    /// <see cref="Warning"/> EntryNotPublished.
    /// </summary>
    public class LessonCatalogEntryView : CatalogEntryView
    {
        #region Public-Members

        /// <summary>
        /// Link identifier (lce_).
        /// </summary>
        public string LinkId { get; set; } = String.Empty;

        /// <summary>
        /// Lesson identifier.
        /// </summary>
        public string LessonId { get; set; } = String.Empty;

        /// <summary>
        /// Order inside the lesson, ascending from 1.
        /// </summary>
        public int Position { get; set; } = 1;

        /// <summary>
        /// Where in the lesson video the technique appears, in seconds, or null.
        /// </summary>
        public int? StartSeconds { get; set; } = null;

        /// <summary>
        /// How the link was created (Editor or Suggested).
        /// </summary>
        public LessonCatalogLinkSourceEnum Source { get; set; } = LessonCatalogLinkSourceEnum.Editor;

        /// <summary>
        /// EntryNotPublished for staff when the entry is not published, otherwise null.
        /// </summary>
        public string? Warning { get; set; } = null;

        /// <summary>
        /// The link's own labels visible to the caller (the entry's labels stay in labels).
        /// </summary>
        public List<string> LinkLabels { get; set; } = new List<string>();

        /// <summary>
        /// The link's own tags visible to the caller (the entry's tags stay in tags).
        /// </summary>
        public Dictionary<string, string> LinkTags { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion
    }
}
