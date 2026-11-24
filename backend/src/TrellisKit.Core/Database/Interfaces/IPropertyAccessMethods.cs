namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Models;

    /// <summary>
    /// The property's stored access configuration (tables propertyaccess and propertyaccessfeatures;
    /// docs/ACCESS_MODELS_CONTRACT.md section 1). Implementations are safe to call concurrently.
    /// </summary>
    public interface IPropertyAccessMethods
    {
        /// <summary>
        /// Read the stored configuration.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Configuration, or null when none is stored yet.</returns>
        Task<AccessPolicy?> ReadAsync(CancellationToken token = default);

        /// <summary>
        /// Insert or replace the stored configuration and its gated features in one transaction.
        /// </summary>
        /// <param name="policy">Configuration.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The policy is null.</exception>
        Task WriteAsync(AccessPolicy policy, CancellationToken token = default);
    }
}
