namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Curriculum data access (tables curricula, curriculumitems, curriculumprogress) and the library progress reads that
    /// satisfy Lesson and Series items. Implementations are bound to one property and safe to call concurrently.
    /// </summary>
    public interface ICurriculumMethods
    {
        /// <summary>
        /// Insert a curriculum.
        /// </summary>
        /// <param name="curriculum">Curriculum.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The curriculum is null.</exception>
        Task CreateAsync(Curriculum curriculum, CancellationToken token = default);

        /// <summary>
        /// Update a curriculum's name, description, field, visibility, position, status, and publication time.
        /// </summary>
        /// <param name="curriculum">Curriculum.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when it exists.</returns>
        /// <exception cref="ArgumentNullException">The curriculum is null.</exception>
        Task<bool> UpdateAsync(Curriculum curriculum, CancellationToken token = default);

        /// <summary>
        /// Read one curriculum with its item count.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Curriculum, or null.</returns>
        Task<Curriculum?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read many curricula.
        /// </summary>
        /// <param name="ids">Identifiers (at most 1000 distinct).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Curricula keyed by identifier.</returns>
        /// <exception cref="ArgumentNullException">The identifiers are null.</exception>
        Task<Dictionary<string, Curriculum>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default);

        /// <summary>
        /// Enumerate curricula.
        /// </summary>
        /// <param name="request">Normalized request (CurriculumCollections).</param>
        /// <param name="filter">Filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<Curriculum>> EnumerateAsync(EnumerationRequest request, CurriculumFilter filter, CancellationToken token = default);

        /// <summary>
        /// A curriculum's items in order (at most 500).
        /// </summary>
        /// <param name="curriculumId">Curriculum.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Items.</returns>
        /// <exception cref="ArgumentNullException">The curriculum is null or empty.</exception>
        Task<List<CurriculumItem>> ReadItemsAsync(string curriculumId, CancellationToken token = default);

        /// <summary>
        /// Replace a curriculum's items (positions follow the list order). Items not in the list are deleted with their
        /// progress, labels, and tags; listed items keep their identifiers and progress. One transaction.
        /// </summary>
        /// <param name="curriculumId">Curriculum.</param>
        /// <param name="items">Items in order.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task ReplaceItemsAsync(string curriculumId, List<CurriculumItem> items, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// A member's progress rows in one curriculum (at most 500).
        /// </summary>
        /// <param name="curriculumId">Curriculum.</param>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows keyed by item.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<Dictionary<string, CurriculumProgress>> ReadProgressAsync(string curriculumId, string userId, CancellationToken token = default);

        /// <summary>
        /// Insert or update a member's progress row of one item.
        /// </summary>
        /// <param name="progress">Progress.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The progress is null.</exception>
        Task UpsertProgressAsync(CurriculumProgress progress, CancellationToken token = default);

        /// <summary>
        /// Delete a member's progress row of one item.
        /// </summary>
        /// <param name="itemId">Item.</param>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a row was deleted.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<bool> DeleteProgressAsync(string itemId, string userId, CancellationToken token = default);

        /// <summary>
        /// The lessons among the given ones the member completed (LessonProgress.Completed).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="lessonIds">Lessons (at most 1000 distinct).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Completed lesson identifiers.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<HashSet<string>> CompletedLessonIdsAsync(string userId, IEnumerable<string> lessonIds, CancellationToken token = default);

        /// <summary>
        /// The series among the given ones with at least one published lesson where the member completed every published lesson.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="seriesIds">Series (at most 1000 distinct).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Completed series identifiers.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<HashSet<string>> CompletedSeriesIdsAsync(string userId, IEnumerable<string> seriesIds, CancellationToken token = default);

        /// <summary>
        /// Titles of published, undeleted lessons or series.
        /// </summary>
        /// <param name="type">Lesson or Series.</param>
        /// <param name="ids">Identifiers (at most 1000 distinct).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Titles keyed by identifier (missing when unavailable).</returns>
        /// <exception cref="ArgumentNullException">The identifiers are null.</exception>
        Task<Dictionary<string, string>> TargetTitlesAsync(CurriculumItemTypeEnum type, IEnumerable<string> ids, CancellationToken token = default);

        /// <summary>
        /// Lessons or series among the given ones that exist (any status, not deleted).
        /// </summary>
        /// <param name="type">Lesson or Series.</param>
        /// <param name="ids">Identifiers (at most 1000 distinct).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Existing identifiers.</returns>
        /// <exception cref="ArgumentNullException">The identifiers are null.</exception>
        Task<HashSet<string>> ExistingTargetsAsync(CurriculumItemTypeEnum type, IEnumerable<string> ids, CancellationToken token = default);

        /// <summary>
        /// A member's progress rows across curricula after an identifier (account export).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="afterId">Identifier position, or null.</param>
        /// <param name="limit">Maximum rows (1 to 1000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows in identifier order.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        Task<List<CurriculumProgress>> ListProgressForUserAsync(string userId, string? afterId, int limit, CancellationToken token = default);

        /// <summary>
        /// Delete a member's progress rows with their labels and tags (account deletion).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        Task EraseForUserAsync(string userId, CancellationToken token = default);
    }
}
