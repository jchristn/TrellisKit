namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Profile value data access (docs/MULTIPROPERTY_CONTRACT.md section 9.7). Implementations are safe to call
    /// concurrently.
    /// </summary>
    public interface IProfileValueMethods
    {
        /// <summary>
        /// Every stored value of one member (bounded by the property's field count).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Values.</returns>
        /// <exception cref="ArgumentNullException">The member id is null or empty.</exception>
        Task<ProfileValueSet> ReadForUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// The values of several members restricted to some fields (cards; bounded by the page size and the card field
        /// limit).
        /// </summary>
        /// <param name="userIds">Members (at most 1000).</param>
        /// <param name="fieldIds">Fields.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Value sets by member id (members without values are absent).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<Dictionary<string, ProfileValueSet>> ReadForUsersAsync(List<string> userIds, List<string> fieldIds, CancellationToken token = default);

        /// <summary>
        /// Replace a member's rows for the written fields in one transaction (delete then insert) and recompute each
        /// written field's valueCount.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="writes">Written fields.</param>
        /// <param name="updatedByUserId">Writer, or null.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task WriteAsync(string userId, List<ProfileFieldWrite> writes, string? updatedByUserId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Delete every profile row of a member and clear references to them in other members' History entries
        /// (account deletion), recomputing every valueCount.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task DeleteForUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Delete every value of a field.
        /// </summary>
        /// <param name="fieldId">Field.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task DeleteForFieldAsync(string fieldId, CancellationToken token = default);

        /// <summary>
        /// Fill statistics of a field.
        /// </summary>
        /// <param name="field">Definition.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Statistics.</returns>
        /// <exception cref="ArgumentNullException">The field is null.</exception>
        Task<ProfileFieldStats> StatsAsync(ProfileField field, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Members with at least one value of a key among the given options (used for OptionInUse checks).
        /// </summary>
        /// <param name="fieldId">Field.</param>
        /// <param name="optionKeys">Option keys.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Keys that have values.</returns>
        Task<List<string>> OptionKeysInUseAsync(string fieldId, List<string> optionKeys, CancellationToken token = default);

        /// <summary>
        /// Backfill the seeded fields from the Phase 1 to 2 columns with set-based statements in one transaction
        /// (idempotent: members that already have a value for a field are skipped), then recompute valueCount.
        /// </summary>
        /// <param name="fields">Seeded field ids.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The field ids are null.</exception>
        Task BackfillLegacyAsync(ProfileLegacyFieldIds fields, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Recompute profilefields.valuecount from the stored values (members with a value or a history entry).
        /// </summary>
        /// <param name="fieldIds">Fields to recount, or null for every field of the property.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task RecountAsync(List<string>? fieldIds, CancellationToken token = default);
    }
}
