namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Enumeration collections of lesson and catalog links (REST_API.md, Lesson techniques): the catalog entries linked
    /// to one lesson and the lessons that teach one catalog entry. Both are capped at 50 per page.
    /// </summary>
    public static class TrainingCollections
    {
        #region Public-Members

        /// <summary>
        /// The catalog entries linked to one lesson ("Techniques in this lesson"), PositionAscending by default. Labels
        /// and tags filter the links.
        /// </summary>
        public static readonly EnumerationCollection LessonCatalogEntries = new EnumerationCollection("lesson-catalog-entries", TargetTypeEnum.LessonCatalogEntry, "lce.id", "lce.createdutc", false)
            .Order("PositionAscending", "lce.position", false, EnumerationKeyKindEnum.Number)
            .Order("NameAscending", "lower(e.name)", false, EnumerationKeyKindEnum.Text)
            .Search("e.name", "e.summary")
            .DefaultTo("PositionAscending");

        /// <summary>
        /// The lessons linked to one catalog entry ("Lessons that teach this"), under the caller's lesson access.
        /// PublishedDescending by default. Labels and tags filter the lessons.
        /// </summary>
        public static readonly EnumerationCollection CatalogEntryLessons = new EnumerationCollection("catalog-entry-lessons", TargetTypeEnum.Lesson, "l.id", "l.createdutc", false)
            .Order("PublishedDescending", "COALESCE(l.publishedutc, l.createdutc)", true, EnumerationKeyKindEnum.Time)
            .Order("TitleAscending", "lower(l.title)", false, EnumerationKeyKindEnum.Text)
            .Search("l.title", "l.summary")
            .DefaultTo("PublishedDescending");

        #endregion
    }
}
