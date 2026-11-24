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
    /// Venue reviews (docs/PHASE4_CONTRACT.md section 3.12). Every write recomputes the venue's rating counters
    /// (ratingcount, ratingtotal) from the live reviews in the same transaction.
    /// </summary>
    public interface IVenueReviewMethods
    {
        /// <summary>
        /// Store a new review.
        /// </summary>
        /// <param name="review">Review.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Stored review.</returns>
        /// <exception cref="ArgumentNullException">The review is null.</exception>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">The member already has a live review of the venue.</exception>
        Task<VenueReview> CreateAsync(VenueReview review, CancellationToken token = default);

        /// <summary>
        /// Read a review in any state (deleted ones included), or null.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Review or null.</returns>
        Task<VenueReview?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read reviews by identifier (deleted ones included).
        /// </summary>
        /// <param name="ids">Identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Reviews.</returns>
        Task<List<VenueReview>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default);

        /// <summary>
        /// A member's live (not deleted) review of a venue, or null.
        /// </summary>
        /// <param name="venueId">Venue.</param>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Review or null.</returns>
        Task<VenueReview?> ReadByAuthorAsync(string venueId, string userId, CancellationToken token = default);

        /// <summary>
        /// Save the rating, body, and edit time of a live review.
        /// </summary>
        /// <param name="review">Review.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Stored review, or null when it no longer exists.</returns>
        /// <exception cref="ArgumentNullException">The review is null.</exception>
        Task<VenueReview?> UpdateAsync(VenueReview review, CancellationToken token = default);

        /// <summary>
        /// Set (create or edit) the instructor reply of a live review.
        /// </summary>
        /// <param name="id">Review.</param>
        /// <param name="body">Reply text.</param>
        /// <param name="userId">Member writing the reply.</param>
        /// <param name="instructorId">Instructor entry of the member.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when saved.</returns>
        Task<bool> SetReplyAsync(string id, string body, string userId, string instructorId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Remove the reply of a review.
        /// </summary>
        /// <param name="id">Review.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a reply was removed.</returns>
        Task<bool> ClearReplyAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Soft delete a live review.
        /// </summary>
        /// <param name="id">Review.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted.</returns>
        Task<bool> SoftDeleteAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Set the moderation state of a review.
        /// </summary>
        /// <param name="id">Review.</param>
        /// <param name="state">State.</param>
        /// <param name="byUserId">Moderator, or null.</param>
        /// <param name="reason">Reason, or null.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when updated.</returns>
        Task<bool> SetModerationAsync(string id, ModerationStateEnum state, string? byUserId, string? reason, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Store the open report count of a review.
        /// </summary>
        /// <param name="id">Review.</param>
        /// <param name="count">Open reports.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task SetOpenReportCountAsync(string id, int count, CancellationToken token = default);

        /// <summary>
        /// Enumerate the reviews of a venue (orderings CreatedDescending, CreatedAscending, RatingDescending, RatingAscending).
        /// </summary>
        /// <param name="request">Prepared request.</param>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<VenueReview>> EnumerateAsync(EnumerationRequest request, VenueContributionFilter filter, CancellationToken token = default);

        /// <summary>
        /// The live reviews of a venue per star rating (keys 1 to 5, always present).
        /// </summary>
        /// <param name="venueId">Venue.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts by rating.</returns>
        Task<Dictionary<int, int>> DistributionAsync(string venueId, CancellationToken token = default);

        /// <summary>
        /// Account deletion: soft delete up to limit reviews by the member and clear up to limit replies they wrote,
        /// recomputing the affected venues.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="limit">Most rows per kind.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows changed.</returns>
        Task<int> EraseForUserAsync(string userId, DateTime nowUtc, int limit, CancellationToken token = default);
    }
}
