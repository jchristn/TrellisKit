namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Links from lessons to the catalog entries they teach (migration 135, table lessoncatalogentries). Lesson access for
    /// "Lessons that teach this" is applied by the library lesson enumeration (<see cref="LessonFilter.CatalogEntryId"/>).
    /// Every statement is bound to the driver's property.
    /// </summary>
    public interface ILessonCatalogLinkMethods
    {
        /// <summary>
        /// Replace every link of a lesson (at most <see cref="LessonCatalogEntry.MaxPerLesson"/>) in one transaction, with
        /// their labels and tags.
        /// </summary>
        /// <param name="lessonId">Lesson.</param>
        /// <param name="links">Links in position order.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored links in position order.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">More than the maximum links.</exception>
        Task<List<LessonCatalogEntry>> ReplaceAsync(string lessonId, List<LessonCatalogEntry> links, CancellationToken token = default);

        /// <summary>
        /// Every link of a lesson in position order (at most <see cref="LessonCatalogEntry.MaxPerLesson"/>).
        /// </summary>
        /// <param name="lessonId">Lesson.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Links.</returns>
        /// <exception cref="ArgumentNullException">The lesson is null or empty.</exception>
        Task<List<LessonCatalogEntry>> ListForLessonAsync(string lessonId, CancellationToken token = default);

        /// <summary>
        /// Enumerate the links of a lesson whose catalog entries are not deleted (and, when publishedOnly, published and not
        /// moderated). Search covers the entry name and summary; labels and tags filter the links.
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="lessonId">Lesson.</param>
        /// <param name="publishedOnly">Only published, unmoderated entries.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of links.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<EnumerationResult<LessonCatalogEntry>> EnumerateForLessonAsync(EnumerationRequest request, string lessonId, bool publishedOnly, CancellationToken token = default);

        /// <summary>
        /// The links between one catalog entry and the given lessons (at most 100 lessons).
        /// </summary>
        /// <param name="catalogEntryId">Catalog entry.</param>
        /// <param name="lessonIds">Lessons.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Links keyed by lesson id.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<Dictionary<string, LessonCatalogEntry>> ForEntryAsync(string catalogEntryId, IReadOnlyList<string> lessonIds, CancellationToken token = default);

        /// <summary>
        /// Delete every link of a lesson (lesson deleted), with their labels and tags.
        /// </summary>
        /// <param name="lessonId">Lesson.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Links removed.</returns>
        /// <exception cref="ArgumentNullException">The lesson is null or empty.</exception>
        Task<int> DeleteForLessonAsync(string lessonId, CancellationToken token = default);

        /// <summary>
        /// Delete every link to a catalog entry (entry deleted), with their labels and tags.
        /// </summary>
        /// <param name="catalogEntryId">Catalog entry.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Links removed.</returns>
        /// <exception cref="ArgumentNullException">The entry is null or empty.</exception>
        Task<int> DeleteForEntryAsync(string catalogEntryId, CancellationToken token = default);
    }
}
