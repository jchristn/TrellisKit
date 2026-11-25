namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Response of POST /api/v1/library/search: one bounded page per kind. Kinds not requested or not available (the
    /// catalog off) are empty, terminal pages. Each page continues through the kind's own enumeration route with the same
    /// search (POST /api/v1/series/enumerate, /api/v1/lessons/enumerate, /api/v1/catalog/entries/enumerate).
    /// </summary>
    public class LibrarySearchResponse
    {
        #region Public-Members

        /// <summary>
        /// Courses (series).
        /// </summary>
        public EnumerationResult<SeriesSummaryView> Series { get; set; } = Empty<SeriesSummaryView>();

        /// <summary>
        /// Lessons.
        /// </summary>
        public EnumerationResult<LessonSummaryView> Lessons { get; set; } = Empty<LessonSummaryView>();

        /// <summary>
        /// Techniques and exercises (published catalog entries).
        /// </summary>
        public EnumerationResult<CatalogEntryView> Techniques { get; set; } = Empty<CatalogEntryView>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// An empty, terminal page.
        /// </summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <returns>Page.</returns>
        public static EnumerationResult<T> Empty<T>()
        {
            return new EnumerationResult<T> { MaxResults = 1, EndOfResults = true, TotalRecords = 0, RecordsRemaining = 0 };
        }

        #endregion
    }
}
