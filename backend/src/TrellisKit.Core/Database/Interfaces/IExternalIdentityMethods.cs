namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Linked external sign-in data access. Implementations are safe to call concurrently.
    /// </summary>
    public interface IExternalIdentityMethods
    {
        /// <summary>
        /// Link an external identity.
        /// </summary>
        /// <param name="identity">Identity.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The identity.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">A unique value is already in use.</exception>
        Task<ExternalIdentity> CreateAsync(ExternalIdentity identity, CancellationToken token = default);

        /// <summary>
        /// Read an identity by provider and subject.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="subject">Provider subject.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Identity, or null.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<ExternalIdentity?> ReadByProviderSubjectAsync(ExternalProviderEnum provider, string subject, CancellationToken token = default);

        /// <summary>
        /// List a member's linked identities.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Identities.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<List<ExternalIdentity>> EnumerateByUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Update an identity.
        /// </summary>
        /// <param name="identity">Identity with changed email, verification, and last used values.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task UpdateAsync(ExternalIdentity identity, CancellationToken token = default);

        /// <summary>
        /// Unlink a member's identity for a provider.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="provider">Provider.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when one was deleted.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<bool> DeleteAsync(string userId, ExternalProviderEnum provider, CancellationToken token = default);

        /// <summary>
        /// Unlink every identity of a member.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<int> DeleteByUserAsync(string userId, CancellationToken token = default);
    }
}
