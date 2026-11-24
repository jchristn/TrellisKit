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
    /// Lesson progress, playback sessions, series follows, and library metrics (docs/PHASE4_CONTRACT.md sections 5.5,
    /// 5.7, 5.10, and resolved decision 17).
    /// </summary>
    public interface ILessonProgressMethods
    {
        /// <summary>
        /// A member's progress on a lesson, or null.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="lessonId">Lesson.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Progress or null.</returns>
        Task<LessonProgress?> ReadAsync(string userId, string lessonId, CancellationToken token = default);

        /// <summary>
        /// A member's progress on several lessons.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="lessonIds">Lessons.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Progress by lesson.</returns>
        Task<Dictionary<string, LessonProgress>> ReadManyAsync(string userId, IEnumerable<string> lessonIds, CancellationToken token = default);

        /// <summary>
        /// Merge one progress update atomically (insert or update in one statement): the resume point and last watched
        /// time move only when the update is not older than the stored client time; the furthest position, watched
        /// seconds, and completion merge monotonically regardless. An update with the same client time as the stored
        /// one is a replay and changes nothing.
        /// </summary>
        /// <param name="merge">Clamped input.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored progress after the merge.</returns>
        Task<LessonProgress> MergeAsync(LessonProgressMerge merge, CancellationToken token = default);

        /// <summary>
        /// Mark a lesson complete (Manual) or not complete (uncomplete), creating the progress row when needed.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="lessonId">Lesson.</param>
        /// <param name="completed">Completed.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored progress.</returns>
        Task<LessonProgress> SetCompletedAsync(string userId, string lessonId, bool completed, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Delete a member's progress on a lesson.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="lessonId">Lesson.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted.</returns>
        Task<bool> DeleteAsync(string userId, string lessonId, CancellationToken token = default);

        /// <summary>
        /// A member's progress on several series in one grouped query (published, visible lessons only).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="seriesIds">Series.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Progress by series (series without progress are absent).</returns>
        Task<Dictionary<string, SeriesProgressSummary>> SeriesProgressAsync(string userId, IEnumerable<string> seriesIds, CancellationToken token = default);

        /// <summary>
        /// Enumerate progress rows.
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<LessonProgress>> EnumerateAsync(EnumerationRequest request, LessonProgressFilter filter, CancellationToken token = default);

        /// <summary>
        /// Record playback seconds in a session (one row per lesson and session).
        /// </summary>
        /// <param name="play">Play (PlayedSeconds is added to the session).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this update started a new session.</returns>
        Task<bool> RecordPlayAsync(LessonPlay play, CancellationToken token = default);

        /// <summary>
        /// Playback sessions per lesson, for one member (or every member when userId is null).
        /// </summary>
        /// <param name="userId">Member, or null.</param>
        /// <param name="lessonIds">Lessons.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Stats by lesson.</returns>
        Task<Dictionary<string, LessonPlayStats>> PlayStatsAsync(string? userId, IEnumerable<string> lessonIds, CancellationToken token = default);

        /// <summary>
        /// Playback sessions of one lesson per member.
        /// </summary>
        /// <param name="lessonId">Lesson.</param>
        /// <param name="userIds">Members.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Stats by member.</returns>
        Task<Dictionary<string, LessonPlayStats>> PlayStatsByUserAsync(string lessonId, IEnumerable<string> userIds, CancellationToken token = default);

        /// <summary>
        /// Follow a series explicitly (clears an earlier opt-out).
        /// </summary>
        /// <param name="follow">Follow.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task FollowAsync(SeriesFollow follow, CancellationToken token = default);

        /// <summary>
        /// Follow a series automatically unless the member has a follow row already (followed or opted out).
        /// </summary>
        /// <param name="follow">Follow (source Progress).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a follow was created.</returns>
        Task<bool> AutoFollowAsync(SeriesFollow follow, CancellationToken token = default);

        /// <summary>
        /// Unfollow a series (the row stays opted out so automatic follows do not return).
        /// </summary>
        /// <param name="seriesId">Series.</param>
        /// <param name="userId">Member.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task UnfollowAsync(string seriesId, string userId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// The series among the identifiers that a member follows.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="seriesIds">Series.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Followed series.</returns>
        Task<HashSet<string>> FollowedAmongAsync(string userId, IEnumerable<string> seriesIds, CancellationToken token = default);

        /// <summary>
        /// Followers of a series in user id order after a cursor.
        /// </summary>
        /// <param name="seriesId">Series.</param>
        /// <param name="afterUserId">Cursor, or null.</param>
        /// <param name="limit">Maximum rows. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Member identifiers.</returns>
        Task<List<string>> FollowerIdsAsync(string seriesId, string? afterUserId, int limit, CancellationToken token = default);

        /// <summary>
        /// Active follows of a member.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountFollowsAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// A member's follow rows after a cursor (export).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="afterId">Cursor, or null.</param>
        /// <param name="limit">Maximum rows. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Follows.</returns>
        Task<List<SeriesFollow>> ListFollowsForUserAsync(string userId, string? afterId, int limit, CancellationToken token = default);

        /// <summary>
        /// A member's progress rows after a cursor (export).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="afterId">Cursor, or null.</param>
        /// <param name="limit">Maximum rows. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Progress rows.</returns>
        Task<List<LessonProgress>> ListForUserAsync(string userId, string? afterId, int limit, CancellationToken token = default);

        /// <summary>
        /// Delete a member's progress, plays, and follows (account deletion).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> DeleteForUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Daily plays (sessions started) or completions in a window.
        /// </summary>
        /// <param name="completions">Completions (true) or plays (false).</param>
        /// <param name="fromUtc">Window start (a UTC midnight).</param>
        /// <param name="days">Days.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One value per day.</returns>
        Task<List<DailyCount>> DailyAsync(bool completions, DateTime fromUtc, int days, CancellationToken token = default);

        /// <summary>
        /// Members who played a lesson in a window.
        /// </summary>
        /// <param name="fromUtc">Window start.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> ActiveLearnersAsync(DateTime fromUtc, CancellationToken token = default);

        /// <summary>
        /// The most played lessons in a window.
        /// </summary>
        /// <param name="fromUtc">Window start.</param>
        /// <param name="limit">Maximum rows. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows.</returns>
        Task<List<LessonMetricRow>> TopLessonsAsync(DateTime fromUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Lessons with plays in a window.
        /// </summary>
        /// <param name="fromUtc">Window start.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountPlayedLessonsAsync(DateTime fromUtc, CancellationToken token = default);

        /// <summary>
        /// Completion rate per series, series with the most learners first.
        /// </summary>
        /// <param name="limit">Maximum rows. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows.</returns>
        Task<List<SeriesCompletionRow>> SeriesCompletionAsync(int limit, CancellationToken token = default);
    }
}
