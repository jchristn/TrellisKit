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
    /// Sign-up code data access. Implementations are safe to call concurrently.
    /// </summary>
    public interface ISignupCodeMethods
    {
        /// <summary>
        /// Insert a code and its tags.
        /// </summary>
        /// <param name="code">Code.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The code.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">A unique value is already in use.</exception>
        Task<SignupCode> CreateAsync(SignupCode code, CancellationToken token = default);

        /// <summary>
        /// Insert a batch and its codes in one transaction.
        /// </summary>
        /// <param name="batch">Batch.</param>
        /// <param name="codes">Codes in the batch.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The batch.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">A unique value is already in use.</exception>
        Task<SignupCodeBatch> CreateBatchAsync(SignupCodeBatch batch, List<SignupCode> codes, CancellationToken token = default);

        /// <summary>
        /// Read a code with its tags.
        /// </summary>
        /// <param name="id">Code identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Code, or null.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<SignupCode?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a code by its text.
        /// </summary>
        /// <param name="code">Code text, compared case-insensitively.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Code, or null.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<SignupCode?> ReadByCodeAsync(string code, CancellationToken token = default);

        /// <summary>
        /// Read several codes.
        /// </summary>
        /// <param name="ids">Code identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Codes found.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<List<SignupCode>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default);

        /// <summary>
        /// Update a code.
        /// </summary>
        /// <param name="code">Code with changed values; tags are replaced.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The code.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">A unique value is already in use.</exception>
        Task<SignupCode> UpdateAsync(SignupCode code, CancellationToken token = default);

        /// <summary>
        /// Delete a code that has never been redeemed.
        /// </summary>
        /// <param name="id">Code identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted; false when the code does not exist or has redemptions.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<bool> DeleteUnredeemedAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate codes (collection <see cref="PhaseOneCollections.SignupCodes"/>).
        /// </summary>
        /// <param name="request">Normalized enumeration.</param>
        /// <param name="filter">Typed filters.</param>
        /// <param name="nowUtc">Current time, used by the status filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<EnumerationResult<SignupCode>> EnumerateAsync(EnumerationRequest request, SignupCodeFilter filter, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Count codes with a derived status.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountByStatusAsync(SignupCodeStatusEnum status, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// In one transaction: re-check that the code is active, started, unexpired, and not exhausted, increment its redemption count, and insert the entitlement and redemption. Safe under concurrency: a single-use code is redeemed at most once.
        /// </summary>
        /// <param name="redemption">Redemption to record.</param>
        /// <param name="entitlement">Entitlement to grant.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when redeemed; false when the code is no longer redeemable.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">A unique value is already in use.</exception>
        Task<bool> TryRedeemAsync(SignupCodeRedemption redemption, Entitlement entitlement, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// In one transaction: re-check that a discount code is still redeemable, increment its redemption count, and insert
        /// the redemption (no entitlement; the discount was applied by Stripe at checkout).
        /// </summary>
        /// <param name="redemption">Redemption to record (EntitlementId empty).</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when recorded; false when the code is no longer redeemable or the member already used it.</returns>
        /// <exception cref="ArgumentNullException">redemption is null.</exception>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">The member already has a redemption of this code.</exception>
        Task<bool> TryRecordDiscountAsync(SignupCodeRedemption redemption, DateTime nowUtc, CancellationToken token = default);
    }
}
