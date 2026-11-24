namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Role assignment data access. Implementations are safe to call concurrently.
    /// </summary>
    public interface IRoleAssignmentMethods
    {
        /// <summary>
        /// List a member's role assignments.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Assignments.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<List<RoleAssignment>> EnumerateByUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// List role assignments for several members.
        /// </summary>
        /// <param name="userIds">Member identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Assignments.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<List<RoleAssignment>> EnumerateByUsersAsync(IEnumerable<string> userIds, CancellationToken token = default);

        /// <summary>
        /// The oldest assignments of a role (notification recipients), at most a limit.
        /// </summary>
        /// <param name="role">Role.</param>
        /// <param name="limit">Most rows (1 to 100).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Assignments.</returns>
        Task<List<RoleAssignment>> EnumerateByRoleAsync(RoleEnum role, int limit, CancellationToken token = default);

        /// <summary>
        /// Replace every role assignment of a member in one transaction.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="assignments">New assignments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task ReplaceForUserAsync(string userId, List<RoleAssignment> assignments, CancellationToken token = default);

        /// <summary>
        /// Add an assignment unless an identical one exists.
        /// </summary>
        /// <param name="assignment">Assignment.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when added.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<bool> AddAsync(RoleAssignment assignment, CancellationToken token = default);
    }
}
