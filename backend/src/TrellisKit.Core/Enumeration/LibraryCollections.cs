namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The library enumeration collections (docs/PHASE4_CONTRACT.md sections 5.1, 5.9, 5.10, 5.12, and resolved
    /// decision 17). Relevance orders search results by the search index rank; without a search it falls back to the
    /// default ordering of the list.
    /// </summary>
    public static class LibraryCollections
    {
        #region Public-Members

        /// <summary>
        /// Member series list.
        /// </summary>
        public static readonly EnumerationCollection Series = SeriesCollection("series", false)
            .Filters("levels", "followedOnly", "progress", "audience")
            .DefaultTo("SortOrderAscending");

        /// <summary>
        /// Admin series list (library staff).
        /// </summary>
        public static readonly EnumerationCollection AdminSeries = SeriesCollection("admin-series", true)
            .Filters("levels", "status", "audience")
            .DefaultTo("SortOrderAscending");

        /// <summary>
        /// Lessons of one series (member).
        /// </summary>
        public static readonly EnumerationCollection SeriesLessons = new EnumerationCollection("series-lessons", TargetTypeEnum.Lesson, "l.id", "l.createdutc", false)
            .Order("PositionAscending", "l.position", false, EnumerationKeyKindEnum.Number)
            .Search("l.title", "l.summary")
            .DefaultTo("PositionAscending");

        /// <summary>
        /// Member lesson list across series.
        /// </summary>
        public static readonly EnumerationCollection Lessons = LessonCollection("lessons", false)
            .Filters("seriesIds", "levels", "kind", "hasResources", "minDurationSeconds", "maxDurationSeconds", "progress", "audience")
            .DefaultTo("PublishedDescending");

        /// <summary>
        /// Admin lesson list (library staff).
        /// </summary>
        public static readonly EnumerationCollection AdminLessons = LessonCollection("admin-lessons", true)
            .Order("PositionAscending", "l.position", false, EnumerationKeyKindEnum.Number)
            .Filters("seriesId", "status", "hasVideo", "mediaStatus", "kind", "levels", "audience")
            .DefaultTo("CreatedDescending");

        /// <summary>
        /// Public sample lessons (anonymous).
        /// </summary>
        public static readonly EnumerationCollection PublicLessons = new EnumerationCollection("public-lessons", TargetTypeEnum.Lesson, "l.id", "l.createdutc", false)
            .Order("PublishedDescending", "COALESCE(l.publishedutc, l.createdutc)", true, EnumerationKeyKindEnum.Time)
            .Search("l.title", "l.summary")
            .DefaultTo("PublishedDescending");

        /// <summary>
        /// Continue watching (the caller's lessons in progress).
        /// </summary>
        public static readonly EnumerationCollection Continue = new EnumerationCollection("library-continue", TargetTypeEnum.LessonProgress, "p.id", "p.createdutc", false)
            .Order("LastWatchedDescending", "p.lastwatchedutc", true, EnumerationKeyKindEnum.Time)
            .DefaultTo("LastWatchedDescending");

        /// <summary>
        /// The caller's progress rows.
        /// </summary>
        public static readonly EnumerationCollection MyProgress = new EnumerationCollection("library-progress", TargetTypeEnum.LessonProgress, "p.id", "p.createdutc", false)
            .Order("LastWatchedDescending", "p.lastwatchedutc", true, EnumerationKeyKindEnum.Time)
            .Filters("completed", "seriesId")
            .DefaultTo("LastWatchedDescending");

        /// <summary>
        /// A member's viewing history (library staff, audited).
        /// </summary>
        public static readonly EnumerationCollection MemberProgress = new EnumerationCollection("admin-member-progress", TargetTypeEnum.LessonProgress, "p.id", "p.createdutc", true)
            .Order("LastWatchedDescending", "p.lastwatchedutc", true, EnumerationKeyKindEnum.Time)
            .Filters("completed", "seriesId")
            .DefaultTo("LastWatchedDescending");

        /// <summary>
        /// The members who watched a lesson (library staff, audited).
        /// </summary>
        public static readonly EnumerationCollection LessonViewers = new EnumerationCollection("admin-lesson-viewers", TargetTypeEnum.LessonProgress, "p.id", "p.createdutc", true)
            .Order("LastWatchedDescending", "p.lastwatchedutc", true, EnumerationKeyKindEnum.Time)
            .Filters("completed")
            .DefaultTo("LastWatchedDescending");

        /// <summary>
        /// The caller's download licenses.
        /// </summary>
        public static readonly EnumerationCollection Downloads = new EnumerationCollection("library-downloads", TargetTypeEnum.LessonDownload, "d.id", "d.createdutc", false)
            .Filters("activeOnly", "deviceId")
            .DefaultTo("CreatedDescending");

        /// <summary>
        /// Library levels (member and admin).
        /// </summary>
        public static readonly EnumerationCollection Levels = LevelCollection("library-levels", false);

        /// <summary>Admin level enumeration with admin page bounds and counts.</summary>
        public static readonly EnumerationCollection AdminLevels = LevelCollection("admin-library-levels", true);

        #endregion

        #region Private-Methods

        private static EnumerationCollection LevelCollection(string name, bool isAdmin)
        {
            return new EnumerationCollection(name, TargetTypeEnum.LibraryLevel, "lv.id", "lv.createdutc", isAdmin)
                .Order("SortOrderAscending", "lv.sortorder", false, EnumerationKeyKindEnum.Number)
                .Order("LabelAscending", "lv.label", false, EnumerationKeyKindEnum.Text)
                .Search("lv.key", "lv.label").Filters("includeRetired").DefaultTo("SortOrderAscending");
        }

        private static EnumerationCollection SeriesCollection(string name, bool isAdmin)
        {
            return new EnumerationCollection(name, TargetTypeEnum.Series, "se.id", "se.createdutc", isAdmin)
                .Order("SortOrderAscending", "se.sortorder", false, EnumerationKeyKindEnum.Number)
                .Order("TitleAscending", "lower(se.title)", false, EnumerationKeyKindEnum.Text)
                .Order("PublishedDescending", "COALESCE(se.publishedutc, se.createdutc)", true, EnumerationKeyKindEnum.Time)
                .Order("Relevance", "se.sortorder", false, EnumerationKeyKindEnum.Number)
                .Search("se.title", "se.summary", "se.description");
        }

        private static EnumerationCollection LessonCollection(string name, bool isAdmin)
        {
            return new EnumerationCollection(name, TargetTypeEnum.Lesson, "l.id", "l.createdutc", isAdmin)
                .Order("PublishedDescending", "COALESCE(l.publishedutc, l.createdutc)", true, EnumerationKeyKindEnum.Time)
                .Order("TitleAscending", "lower(l.title)", false, EnumerationKeyKindEnum.Text)
                .Order("DurationAscending", "l.durationseconds", false, EnumerationKeyKindEnum.Number)
                .Order("Relevance", "COALESCE(l.publishedutc, l.createdutc)", true, EnumerationKeyKindEnum.Time)
                .Search("l.title", "l.summary", "l.description");
        }

        #endregion
    }
}
