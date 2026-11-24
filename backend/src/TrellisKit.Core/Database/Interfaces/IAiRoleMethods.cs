namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Task role executor hierarchies (docs/INTEGRATION.md). Thread safety: safe for concurrent use.
    /// </summary>
    public interface IAiRoleMethods
    {
        /// <summary>
        /// Every configured role with its executors in order.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Roles by key.</returns>
        Task<Dictionary<string, AiTaskRoleConfig>> ReadAllAsync(CancellationToken token = default);

        /// <summary>
        /// Replace a role's policies and executors.
        /// </summary>
        /// <param name="config">Role configuration (executors at most 8).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">config is null.</exception>
        Task SaveAsync(AiTaskRoleConfig config, CancellationToken token = default);
    }
}
