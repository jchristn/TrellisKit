namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Library configuration, levels, series, lessons, and their child lists (docs/PHASE4_CONTRACT.md section 14).
    /// Counters (lessoncount, totaldurationseconds) are recomputed from source rows in the same transaction as each
    /// lesson write.
    /// </summary>
    public interface ILibraryCatalogMethods
    {
        /// <summary>
        /// The stored library configuration, or null before seeding.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Configuration or null.</returns>
        Task<LibraryConfig?> ReadConfigAsync(CancellationToken token = default);

        /// <summary>
        /// Store the library configuration (upsert).
        /// </summary>
        /// <param name="config">Configuration.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task WriteConfigAsync(LibraryConfig config, CancellationToken token = default);

        /// <summary>
        /// Custom levels in sort order (at most 200).
        /// </summary>
        /// <param name="includeRetired">Include retired levels.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Levels.</returns>
        Task<List<LibraryLevel>> ListLevelsAsync(bool includeRetired, CancellationToken token = default);

        /// <summary>
        /// Read a custom level, or null.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Level or null.</returns>
        Task<LibraryLevel?> ReadLevelAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Create a custom level.
        /// </summary>
        /// <param name="level">Level.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Level.</returns>
        /// <exception cref="DuplicateRecordException">The key is in use.</exception>
        Task<LibraryLevel> CreateLevelAsync(LibraryLevel level, CancellationToken token = default);

        /// <summary>
        /// Store a custom level's label, color, sort order, and retired flag.
        /// </summary>
        /// <param name="level">Level.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when found.</returns>
        Task<bool> UpdateLevelAsync(LibraryLevel level, CancellationToken token = default);

        /// <summary>
        /// Delete a custom level.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted.</returns>
        Task<bool> DeleteLevelAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Replace every custom level (seeding from property.json).
        /// </summary>
        /// <param name="levels">Levels.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task ReplaceLevelsAsync(List<LibraryLevel> levels, CancellationToken token = default);

        /// <summary>
        /// Create a series with its levels.
        /// </summary>
        /// <param name="series">Series.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Series.</returns>
        /// <exception cref="DuplicateRecordException">The slug is in use.</exception>
        Task<LibrarySeries> CreateSeriesAsync(LibrarySeries series, CancellationToken token = default);

        /// <summary>
        /// Read an undeleted series, or null.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Series or null.</returns>
        Task<LibrarySeries?> ReadSeriesAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read an undeleted series by slug, following slug redirects, or null.
        /// </summary>
        /// <param name="slug">Slug (case-insensitive).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Series or null.</returns>
        Task<LibrarySeries?> ReadSeriesBySlugAsync(string slug, CancellationToken token = default);

        /// <summary>
        /// Read several series (deleted included).
        /// </summary>
        /// <param name="ids">Identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Series.</returns>
        Task<List<LibrarySeries>> ReadSeriesManyAsync(IEnumerable<string> ids, CancellationToken token = default);

        /// <summary>
        /// Store a series' fields and levels; records a slug redirect when the slug changed and, when narrowFrom is set,
        /// narrows every lesson of the series whose audience equals narrowFrom to the series' new audience.
        /// </summary>
        /// <param name="series">Series.</param>
        /// <param name="previousSlug">The slug before the change.</param>
        /// <param name="narrowFrom">Old audience whose lessons follow the new audience, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Series.</returns>
        /// <exception cref="DuplicateRecordException">The slug is in use.</exception>
        Task<LibrarySeries> UpdateSeriesAsync(LibrarySeries series, string previousSlug, ContentAudienceEnum? narrowFrom, CancellationToken token = default);

        /// <summary>
        /// Undeleted series.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountSeriesAsync(CancellationToken token = default);

        /// <summary>
        /// The sort order that places a new series last.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Sort order.</returns>
        Task<int> NextSeriesSortOrderAsync(CancellationToken token = default);

        /// <summary>
        /// Identifiers of undeleted, non-archived series in sort order.
        /// </summary>
        /// <param name="limit">Maximum rows. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Identifiers.</returns>
        Task<List<string>> ActiveSeriesIdsAsync(int limit, CancellationToken token = default);

        /// <summary>
        /// Assign sort orders 10, 20, 30 in list order.
        /// </summary>
        /// <param name="seriesIds">Series in order.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows updated.</returns>
        Task<int> ReorderSeriesAsync(List<string> seriesIds, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Change a series' status: Published (keeps the first publication time), Archived (sets archivedutc), or
        /// Draft; unarchive passes Published or Draft.
        /// </summary>
        /// <param name="id">Series.</param>
        /// <param name="status">New status.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when found.</returns>
        Task<bool> SetSeriesStatusAsync(string id, LibraryStatusEnum status, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Soft-delete a series that has no undeleted lessons.
        /// </summary>
        /// <param name="id">Series.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted; false when it still has lessons or does not exist.</returns>
        Task<bool> DeleteSeriesAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Enumerate series.
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<LibrarySeries>> EnumerateSeriesAsync(EnumerationRequest request, SeriesFilter filter, CancellationToken token = default);

        /// <summary>
        /// Create a lesson with its child lists at the end of its series (or at lesson.Position when it is 1 or more,
        /// shifting later lessons) and recompute the series counters.
        /// </summary>
        /// <param name="lesson">Lesson.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Lesson.</returns>
        /// <exception cref="DuplicateRecordException">The slug is in use in the series.</exception>
        Task<Lesson> CreateLessonAsync(Lesson lesson, CancellationToken token = default);

        /// <summary>
        /// Read an undeleted lesson with its child lists, or null.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Lesson or null.</returns>
        Task<Lesson?> ReadLessonAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read an undeleted lesson by slug (newest first when several series use it; slug redirects followed), or null.
        /// </summary>
        /// <param name="slug">Slug (case-insensitive).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Lesson or null.</returns>
        Task<Lesson?> ReadLessonBySlugAsync(string slug, CancellationToken token = default);

        /// <summary>
        /// Read several lessons (deleted included).
        /// </summary>
        /// <param name="ids">Identifiers.</param>
        /// <param name="children">Load markers, resources, and captions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Lessons.</returns>
        Task<List<Lesson>> ReadLessonsAsync(IEnumerable<string> ids, bool children, CancellationToken token = default);

        /// <summary>
        /// Store a lesson's fields and child lists (lists replaced), record a slug redirect when the slug changed, and
        /// recompute the series counters.
        /// </summary>
        /// <param name="lesson">Lesson.</param>
        /// <param name="previousSlug">Slug before the change.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Lesson.</returns>
        /// <exception cref="DuplicateRecordException">The slug is in use in the series.</exception>
        Task<Lesson> UpdateLessonAsync(Lesson lesson, string previousSlug, CancellationToken token = default);

        /// <summary>
        /// Change a lesson's status and schedule and recompute the series counters. Published keeps the first
        /// publication time; Archived sets archivedutc; any other status clears archivedutc.
        /// </summary>
        /// <param name="id">Lesson.</param>
        /// <param name="status">New status.</param>
        /// <param name="publishAtUtc">Schedule (Scheduled), or null.</param>
        /// <param name="expected">Only change when the current status is one of these (empty for any).</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when changed.</returns>
        Task<bool> SetLessonStatusAsync(string id, LibraryStatusEnum status, DateTime? publishAtUtc, IReadOnlyCollection<LibraryStatusEnum> expected, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Soft-delete a lesson, close the gap in its series' positions, and recompute the counters.
        /// </summary>
        /// <param name="id">Lesson.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted.</returns>
        Task<bool> DeleteLessonAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Move a lesson to a series and position (1-based, clamped), closing the gap in the old series and recomputing
        /// both series' counters.
        /// </summary>
        /// <param name="id">Lesson.</param>
        /// <param name="seriesId">Target series.</param>
        /// <param name="position">Target position.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when moved.</returns>
        /// <exception cref="DuplicateRecordException">The slug is in use in the target series.</exception>
        Task<bool> MoveLessonAsync(string id, string seriesId, int position, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Assign positions 1, 2, 3 in list order inside a series.
        /// </summary>
        /// <param name="seriesId">Series.</param>
        /// <param name="lessonIds">Lessons in order.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows updated.</returns>
        Task<int> ReorderLessonsAsync(string seriesId, List<string> lessonIds, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Identifiers of a series' undeleted lessons in position order.
        /// </summary>
        /// <param name="seriesId">Series.</param>
        /// <param name="limit">Maximum rows. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Identifiers.</returns>
        Task<List<string>> LessonIdsOfSeriesAsync(string seriesId, int limit, CancellationToken token = default);

        /// <summary>
        /// The published lessons right before and after a position in a series (null when none).
        /// </summary>
        /// <param name="seriesId">Series.</param>
        /// <param name="position">Position.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Two entries: previous and next (either may be null).</returns>
        Task<List<Lesson?>> NeighborsAsync(string seriesId, int position, CancellationToken token = default);

        /// <summary>
        /// Enumerate lessons.
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page (without child lists).</returns>
        Task<EnumerationResult<Lesson>> EnumerateLessonsAsync(EnumerationRequest request, LessonFilter filter, CancellationToken token = default);

        /// <summary>
        /// Scheduled and Processing lessons that are due (publishAtUtc at or before now, or none), oldest first.
        /// </summary>
        /// <param name="nowUtc">Now.</param>
        /// <param name="limit">Maximum rows. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Lessons (without child lists).</returns>
        Task<List<Lesson>> DueLessonsAsync(DateTime nowUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Undeleted lessons that use a media as video, document resource, or caption.
        /// </summary>
        /// <param name="mediaId">Media.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Lesson identifiers.</returns>
        Task<List<string>> LessonIdsUsingMediaAsync(string mediaId, CancellationToken token = default);

        /// <summary>
        /// Set a lesson's duration, clamp every progress position past it, and recompute the series counters.
        /// </summary>
        /// <param name="id">Lesson.</param>
        /// <param name="seconds">Duration.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when found.</returns>
        Task<bool> SetLessonDurationAsync(string id, int seconds, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Recompute a lesson's comment count and reaction total from the comments and reactions tables.
        /// </summary>
        /// <param name="id">Lesson.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task RecomputeEngagementAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Recompute a lesson's open report count from the reports table.
        /// </summary>
        /// <param name="id">Lesson.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task RecomputeReportsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Set a lesson's moderation state and recompute the series counters.
        /// </summary>
        /// <param name="id">Lesson.</param>
        /// <param name="state">State.</param>
        /// <param name="byUserId">Moderator, or null.</param>
        /// <param name="reason">Reason, or null.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when found.</returns>
        Task<bool> SetLessonModerationAsync(string id, ModerationStateEnum state, string? byUserId, string? reason, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Set or clear the feed card of a lesson promotion.
        /// </summary>
        /// <param name="id">Lesson.</param>
        /// <param name="feedPostId">Feed post, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task SetLessonPromotionAsync(string id, string? feedPostId, CancellationToken token = default);

        /// <summary>
        /// Lessons (or series) changed at or after a time (search sweeps), at most limit, deleted included.
        /// </summary>
        /// <param name="series">Series (true) or lessons (false).</param>
        /// <param name="sinceUtc">Changed at or after, or null for all.</param>
        /// <param name="limit">Maximum rows. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Identifiers.</returns>
        Task<List<string>> ChangedIdsAsync(bool series, DateTime? sinceUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Raise or aggregate the unread NewLessonInSeries notifications of series followers: recipients who already
        /// have an unread one for the series get it updated in place (count, title, path, push once per hour).
        /// </summary>
        /// <param name="seriesId">Series.</param>
        /// <param name="recipientUserIds">Followers in this batch.</param>
        /// <param name="seriesTitle">Series title.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The recipients whose notification was aggregated.</returns>
        Task<List<string>> AggregateNewLessonNotificationsAsync(string seriesId, IReadOnlyCollection<string> recipientUserIds, string seriesTitle, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Active library staff (SystemAdmin and LibraryEditor holders, not deleted or banned).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Member identifiers (at most 500).</returns>
        Task<List<string>> StaffIdsAsync(CancellationToken token = default);
    }
}
