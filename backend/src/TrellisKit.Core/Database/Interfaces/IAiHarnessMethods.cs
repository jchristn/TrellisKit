namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Harness profiles (docs/HARNESSES.md): one row per harness runtime of a property, a bounded set.
    /// </summary>
    public interface IAiHarnessMethods
    {
        /// <summary>
        /// Read every saved profile of the property (at most one per runtime).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Profiles.</returns>
        Task<List<AiHarnessProfile>> ReadAllAsync(CancellationToken token = default);

        /// <summary>
        /// Read one runtime's profile.
        /// </summary>
        /// <param name="runtime">Runtime.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Profile, or null when none was saved.</returns>
        Task<AiHarnessProfile?> ReadAsync(AiExecutorRuntimeEnum runtime, CancellationToken token = default);

        /// <summary>
        /// Create or replace a runtime's profile.
        /// </summary>
        /// <param name="profile">Profile.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">profile is null.</exception>
        Task SaveAsync(AiHarnessProfile profile, CancellationToken token = default);
    }
}
