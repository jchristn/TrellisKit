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
    /// Instructors, their social links, slug redirects, and member links.
    /// </summary>
    public interface IInstructorMethods
    {
        /// <summary>
        /// The statements that insert an instructor and its social links.
        /// </summary>
        /// <param name="instructor">Instructor.</param>
        /// <returns>Statements.</returns>
        /// <exception cref="ArgumentNullException">The instructor is null.</exception>
        List<string> CreateStatements(Instructor instructor);

        /// <summary>
        /// The statements that update an instructor and replace its social links.
        /// </summary>
        /// <param name="instructor">Instructor.</param>
        /// <param name="previousSlug">Slug before the change, or null.</param>
        /// <returns>Statements.</returns>
        /// <exception cref="ArgumentNullException">The instructor is null.</exception>
        List<string> UpdateStatements(Instructor instructor, string? previousSlug);

        /// <summary>
        /// Create an instructor.
        /// </summary>
        /// <param name="instructor">Instructor.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored instructor.</returns>
        /// <exception cref="ArgumentNullException">The instructor is null.</exception>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">The slug or linked member is in use.</exception>
        Task<Instructor> CreateAsync(Instructor instructor, CancellationToken token = default);

        /// <summary>
        /// Update an instructor.
        /// </summary>
        /// <param name="instructor">Instructor.</param>
        /// <param name="previousSlug">Slug before the change, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored instructor, or null when deleted.</returns>
        /// <exception cref="ArgumentNullException">The instructor is null.</exception>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">The slug is in use.</exception>
        Task<Instructor?> UpdateAsync(Instructor instructor, string? previousSlug, CancellationToken token = default);

        /// <summary>
        /// Read a live instructor.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Instructor, or null.</returns>
        Task<Instructor?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a live instructor by its slug or a previous slug.
        /// </summary>
        /// <param name="slug">Slug.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Instructor, or null.</returns>
        Task<Instructor?> ReadBySlugAsync(string slug, CancellationToken token = default);

        /// <summary>
        /// Read live instructors by identifier.
        /// </summary>
        /// <param name="ids">Identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Instructors.</returns>
        /// <exception cref="ArgumentNullException">The ids are null.</exception>
        Task<List<Instructor>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default);

        /// <summary>
        /// The live instructor linked to a member, if any.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Instructor, or null.</returns>
        Task<Instructor?> ReadByLinkedUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Enumerate instructors.
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="filter">Filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<Instructor>> EnumerateAsync(EnumerationRequest request, InstructorFilter filter, CancellationToken token = default);

        /// <summary>
        /// Set or clear the linked member.
        /// </summary>
        /// <param name="id">Instructor.</param>
        /// <param name="userId">Member, or null to unlink.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when changed.</returns>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">The member is linked to another entry.</exception>
        Task<bool> SetLinkedUserAsync(string id, string? userId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// The statement that links an instructor to a member when it is unclaimed (claim approvals).
        /// </summary>
        /// <param name="id">Instructor.</param>
        /// <param name="userId">Member.</param>
        /// <param name="nowUtc">Now.</param>
        /// <returns>Statement.</returns>
        string LinkUserStatement(string id, string userId, DateTime nowUtc);

        /// <summary>
        /// Set the lifecycle status.
        /// </summary>
        /// <param name="id">Instructor.</param>
        /// <param name="status">Status.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the instructor exists.</returns>
        Task<bool> SetStatusAsync(string id, InstructorStatusEnum status, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Set the moderation state.
        /// </summary>
        /// <param name="id">Instructor.</param>
        /// <param name="state">State.</param>
        /// <param name="byUserId">Moderator, or null.</param>
        /// <param name="reason">Reason, or null.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the instructor exists.</returns>
        Task<bool> SetModerationAsync(string id, ModerationStateEnum state, string? byUserId, string? reason, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Set the open report count.
        /// </summary>
        /// <param name="id">Instructor.</param>
        /// <param name="count">Open reports.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task SetOpenReportCountAsync(string id, int count, CancellationToken token = default);

        /// <summary>
        /// Soft delete an instructor: links to venues are removed and the venues' counters recomputed.
        /// </summary>
        /// <param name="id">Instructor.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted.</returns>
        Task<bool> SoftDeleteAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Remove a member's links to instructor entries (account deletion); the entries stay.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Instructors unlinked.</returns>
        Task<List<string>> UnlinkUserAsync(string userId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Duplicate candidates: live instructors whose name contains a word, bounded.
        /// </summary>
        /// <param name="nameWords">Lowercase words (any matches).</param>
        /// <param name="limit">Most candidates.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Instructors.</returns>
        Task<List<Instructor>> DuplicateCandidatesAsync(IEnumerable<string> nameWords, int limit, CancellationToken token = default);

        /// <summary>
        /// A page of live instructors by identifier (exports), every status.
        /// </summary>
        /// <param name="afterId">Exclusive position, or null.</param>
        /// <param name="limit">Most instructors.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Instructors.</returns>
        Task<List<Instructor>> ListPageAsync(string? afterId, int limit, CancellationToken token = default);

        /// <summary>
        /// The live venues an instructor is linked to (bounded).
        /// </summary>
        /// <param name="id">Instructor.</param>
        /// <param name="limit">Most venues.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Venue identifiers.</returns>
        Task<List<string>> VenueIdsAsync(string id, int limit, CancellationToken token = default);
    }
}
