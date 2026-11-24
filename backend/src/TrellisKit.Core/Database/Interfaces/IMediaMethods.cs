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
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Media attachment and variant data access. Implementations are safe to call concurrently; status transitions
    /// and attachment are conditional updates, so concurrent callers cannot both win.
    /// </summary>
    public interface IMediaMethods
    {
        /// <summary>
        /// Insert a media row.
        /// </summary>
        /// <param name="media">Media.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The media.</returns>
        /// <exception cref="ArgumentNullException">The media is null or has no id.</exception>
        /// <exception cref="DuplicateRecordException">The id exists.</exception>
        Task<MediaAttachment> CreateAsync(MediaAttachment media, CancellationToken token = default);

        /// <summary>
        /// Read one media row (deleted rows included; check <see cref="MediaAttachment.IsDeleted"/>).
        /// </summary>
        /// <param name="id">Media identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Media, or null.</returns>
        Task<MediaAttachment?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read several media rows (deleted rows included), in no particular order.
        /// </summary>
        /// <param name="ids">Identifiers (bounded by the caller).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows found.</returns>
        /// <exception cref="ArgumentNullException">The ids are null.</exception>
        Task<List<MediaAttachment>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default);

        /// <summary>
        /// Read the media row of an asynchronous transcode job.
        /// </summary>
        /// <param name="jobId">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Media, or null.</returns>
        Task<MediaAttachment?> ReadByJobIdAsync(string jobId, CancellationToken token = default);

        /// <summary>
        /// Write every mutable column of a media row, optionally only while it still has an expected status.
        /// </summary>
        /// <param name="media">Media with the new values (UpdatedUtc is written as given).</param>
        /// <param name="expectedStatus">Status the stored row must have, or null for unconditional.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the row was updated.</returns>
        /// <exception cref="ArgumentNullException">The media is null.</exception>
        Task<bool> UpdateAsync(MediaAttachment media, MediaStatusEnum? expectedStatus = null, CancellationToken token = default);

        /// <summary>
        /// Claim Processing rows of a kind whose claim is free or lapsed (conditional update), oldest first.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <param name="holder">Worker instance.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="claimUntilUtc">Claim end.</param>
        /// <param name="limit">Most rows to claim.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Claimed rows.</returns>
        Task<List<MediaAttachment>> ClaimAsync(MediaKindEnum kind, string holder, DateTime nowUtc, DateTime claimUntilUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// In one transaction, replace a media item's variants and write the row while it is still Processing.
        /// </summary>
        /// <param name="media">Media with its Ready values.</param>
        /// <param name="variants">Variants (bounded: three sizes in two formats).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the row was still Processing and was written.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<bool> CompleteProcessingAsync(MediaAttachment media, List<MediaVariant> variants, CancellationToken token = default);

        /// <summary>
        /// Variants of one media item (at most six).
        /// </summary>
        /// <param name="mediaId">Media identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Variants.</returns>
        Task<List<MediaVariant>> ReadVariantsAsync(string mediaId, CancellationToken token = default);

        /// <summary>
        /// Variants of several media items, keyed by media identifier.
        /// </summary>
        /// <param name="mediaIds">Identifiers (bounded by the caller).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Variants by media identifier (items without variants are absent).</returns>
        /// <exception cref="ArgumentNullException">The ids are null.</exception>
        Task<Dictionary<string, List<MediaVariant>>> ReadVariantsManyAsync(IEnumerable<string> mediaIds, CancellationToken token = default);

        /// <summary>
        /// Delete a media item's variant rows.
        /// </summary>
        /// <param name="mediaId">Media identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task DeleteVariantsAsync(string mediaId, CancellationToken token = default);

        /// <summary>
        /// In one transaction: detach media of the target that are not in the list (detachedutc set), then attach the
        /// listed media in order (positions 0, 1, ...) where each is unattached or already attached to the same
        /// target and not deleted.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="orderedIds">Media identifiers in display order (bounded by the attachment limits).</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The target's attachments afterwards, in order.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<List<MediaAttachment>> AttachAsync(TargetTypeEnum targetType, string targetId, List<string> orderedIds, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Detach media from a target (detachedutc set).
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="mediaIds">Only these media, or null for all of the target's media.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows detached.</returns>
        /// <exception cref="ArgumentNullException">The target identifier is null or empty.</exception>
        Task<int> DetachAsync(TargetTypeEnum targetType, string targetId, IEnumerable<string>? mediaIds, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Attachments of many targets in one query, ordered by target and position (bounded by the attachment limits
        /// per target and by the caller's page of targets).
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetIds">Target identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows.</returns>
        /// <exception cref="ArgumentNullException">The ids are null.</exception>
        Task<List<MediaAttachment>> ListByTargetsAsync(TargetTypeEnum targetType, IEnumerable<string> targetIds, CancellationToken token = default);

        /// <summary>
        /// A member's media that is not deleted, oldest first, at most limit rows (callers loop for account deletion).
        /// </summary>
        /// <param name="userId">Owner.</param>
        /// <param name="unattachedOnly">Only unattached media.</param>
        /// <param name="limit">Most rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows.</returns>
        Task<List<MediaAttachment>> ListByOwnerAsync(string userId, bool unattachedOnly, int limit, CancellationToken token = default);

        /// <summary>
        /// Bytes of upload sessions a member created since a time (deleted rows included, so deleting does not reset the quota).
        /// </summary>
        /// <param name="userId">Owner.</param>
        /// <param name="sinceUtc">Start (UTC midnight for the daily quota).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Bytes.</returns>
        Task<long> SumBytesSinceAsync(string userId, DateTime sinceUtc, CancellationToken token = default);

        /// <summary>
        /// Uploading sessions that expired, oldest first.
        /// </summary>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="limit">Most rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows.</returns>
        Task<List<MediaAttachment>> ListExpiredUploadsAsync(DateTime nowUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Ready or Failed media that is not attached, not deleted, and was created (or last detached) before a cutoff.
        /// </summary>
        /// <param name="cutoffUtc">Cutoff.</param>
        /// <param name="limit">Most rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows.</returns>
        Task<List<MediaAttachment>> ListUnattachedBeforeAsync(DateTime cutoffUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Remove deleted rows (and their variants) deleted before a cutoff.
        /// </summary>
        /// <param name="cutoffUtc">Cutoff.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows removed.</returns>
        Task<int> PurgeDeletedAsync(DateTime cutoffUtc, CancellationToken token = default);

        /// <summary>
        /// Admin library enumeration (collection <see cref="MediaCollections.AdminLibrary"/>; deleted rows excluded).
        /// </summary>
        /// <param name="request">Normalized enumeration request.</param>
        /// <param name="filter">Typed filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<MediaAttachment>> EnumerateAsync(EnumerationRequest request, MediaFilter filter, CancellationToken token = default);

        /// <summary>
        /// Mark a media row deleted and, in the same transaction, delete its variants and its labels and tags.
        /// </summary>
        /// <param name="mediaId">Media identifier.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a row that was not yet deleted was marked.</returns>
        Task<bool> MarkDeletedAsync(string mediaId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Admin library totals.
        /// </summary>
        /// <param name="nowUtc">Current time (for failed24h).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summary.</returns>
        Task<MediaSummary> SummaryAsync(DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// A keyset batch of a member's media rows in any state (including deleted), by identifier (account export).
        /// </summary>
        /// <param name="ownerUserId">Owner.</param>
        /// <param name="afterId">Continue after this identifier, or null.</param>
        /// <param name="limit">Most rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Media in identifier order.</returns>
        /// <exception cref="ArgumentNullException">The owner is null or empty.</exception>
        Task<List<MediaAttachment>> ListByOwnerAfterAsync(string ownerUserId, string? afterId, int limit, CancellationToken token = default);

        /// <summary>
        /// Bind private media to a scope (a ticket or conversation): sets scopeid on the items of the purpose whose scope is
        /// still pending or already this scope.
        /// </summary>
        /// <param name="mediaIds">Media identifiers.</param>
        /// <param name="purpose">Private purpose the items must have.</param>
        /// <param name="scopeId">Scope identifier.</param>
        /// <param name="nowUtc">Update time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Items bound (or already bound to this scope).</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<int> BindScopeAsync(IEnumerable<string> mediaIds, MediaPurposeEnum purpose, string scopeId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Total stored bytes of the property's media that is not deleted.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Bytes.</returns>
        Task<long> TotalBytesAsync(CancellationToken token = default);
    }
}
