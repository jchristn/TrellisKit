namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Sign-up code redemption data access. Implementations are safe to call concurrently.
    /// </summary>
    public interface ISignupCodeRedemptionMethods
    {
        /// <summary>
        /// Read a redemption.
        /// </summary>
        /// <param name="id">Redemption identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Redemption, or null.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<SignupCodeRedemption?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a member's redemption of a code.
        /// </summary>
        /// <param name="signupCodeId">Code identifier.</param>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Redemption, or null.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<SignupCodeRedemption?> ReadByCodeAndUserAsync(string signupCodeId, string userId, CancellationToken token = default);

        /// <summary>
        /// Enumerate redemptions (collection <see cref="PhaseOneCollections.Redemptions"/>).
        /// </summary>
        /// <param name="request">Normalized enumeration.</param>
        /// <param name="filter">Typed filters.</param>
        /// <param name="nowUtc">Current time, used by the status filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<EnumerationResult<SignupCodeRedemption>> EnumerateAsync(EnumerationRequest request, RedemptionFilter filter, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// List a member's redemptions, newest first.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Redemptions.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<List<SignupCodeRedemption>> EnumerateByUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// One keyset batch of a member's sign-up code redemptions, ordered by id (identifiers are K-sortable), for streaming exports.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="afterId">Return records with an id after this one, or null for the first batch.</param>
        /// <param name="limit">Batch size, 1 to 1000.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Up to limit records.</returns>
        /// <exception cref="ArgumentNullException">The member identifier is null or empty.</exception>
        Task<List<SignupCodeRedemption>> EnumerateByUserAfterAsync(string userId, string? afterId, int limit, CancellationToken token = default);

        /// <summary>
        /// Update a redemption.
        /// </summary>
        /// <param name="redemption">Redemption with changed revocation and reminder values.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task UpdateAsync(SignupCodeRedemption redemption, CancellationToken token = default);

        /// <summary>
        /// List unrevoked redemptions whose free membership ends between now and the horizon.
        /// </summary>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="horizonUtc">Latest membership end to include.</param>
        /// <param name="belowStage">Only redemptions whose reminder stage is below this value.</param>
        /// <param name="limit">Maximum rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Redemptions.</returns>
        Task<List<SignupCodeRedemption>> EnumerateDueForReminderAsync(DateTime nowUtc, DateTime horizonUtc, int belowStage, int limit, CancellationToken token = default);

        /// <summary>
        /// Atomically advance a redemption's reminder stage, so each reminder is sent once even with several workers.
        /// </summary>
        /// <param name="id">Redemption identifier.</param>
        /// <param name="stage">New stage; applied only when the current stage is lower.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this call advanced it.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<bool> TrySetReminderStageAsync(string id, int stage, CancellationToken token = default);

        /// <summary>
        /// Count redemptions.
        /// </summary>
        /// <param name="signupCodeId">Code identifier, or null for every code.</param>
        /// <param name="fromUtc">Only redemptions at or after this time, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountAsync(string? signupCodeId, DateTime? fromUtc, CancellationToken token = default);

        /// <summary>
        /// Count redemptions per UTC day from a start time. Days without redemptions are omitted.
        /// </summary>
        /// <param name="signupCodeId">Code identifier, or null for every code.</param>
        /// <param name="fromUtc">Window start (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts by day, oldest first.</returns>
        Task<List<DailyCount>> CountByDayAsync(string? signupCodeId, DateTime fromUtc, CancellationToken token = default);

        /// <summary>
        /// Count redemption outcomes: active, converted to paid, lapsed, and revoked.
        /// </summary>
        /// <param name="signupCodeId">Code identifier, or null for every code.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Outcomes.</returns>
        Task<RedemptionOutcomes> CountOutcomesAsync(string? signupCodeId, DateTime nowUtc, CancellationToken token = default);
    }
}
