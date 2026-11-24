namespace TrellisKit.Server.Services.Realtime
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Models;

    /// <summary>
    /// The deployment-wide connection registry of one property (realtimeconnections, docs/PHASE5_CONTRACT.md section
    /// 4.11): the gateway upserts a row on hello and state, touches it every 60 seconds, and deletes it on disconnect;
    /// <c>RealtimeRegistryPruneWorker</c> removes rows not seen for 5 minutes. The messaging push decision calls
    /// <see cref="ForegroundAsync"/> so a member connected to task A suppresses pushes sent from a write on task B.
    /// Thread safety: safe to use concurrently.
    /// </summary>
    public sealed class RealtimeConnectionRegistry
    {
        #region Public-Members

        /// <summary>
        /// A row last seen longer ago than this counts as offline. Default 90. Minimum 10, maximum 600.
        /// </summary>
        public int OnlineSeconds
        {
            get { return _OnlineSeconds; }
            set { _OnlineSeconds = Math.Clamp(value, 10, 600); }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private int _OnlineSeconds = 90;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the registry.
        /// </summary>
        /// <param name="database">Property-bound driver.</param>
        /// <exception cref="ArgumentNullException">The driver is null.</exception>
        public RealtimeConnectionRegistry(DatabaseDriverBase database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a member has a foreground connection on any task, and whether one has a conversation open.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="conversationId">Conversation to test for activeConversationId, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        public async Task<RealtimeForeground> ForegroundAsync(string userId, string? conversationId = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            List<RealtimeConnectionRecord> rows = await _Database.RealtimeConnections.EnumerateRecentByUserAsync(userId, DateTime.UtcNow.AddSeconds(-_OnlineSeconds), token).ConfigureAwait(false);
            List<RealtimeConnectionRecord> foreground = rows.Where(r => String.Equals(r.AppState, "Foreground", StringComparison.Ordinal)).ToList();
            return new RealtimeForeground
            {
                RecentConnections = rows.Count,
                HasForeground = foreground.Count > 0,
                ConversationActive = conversationId != null && foreground.Any(r => String.Equals(r.ActiveConversationId, conversationId, StringComparison.Ordinal))
            };
        }

        /// <summary>
        /// The member's connection rows seen within <see cref="OnlineSeconds"/> (staff diagnostics and tests).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows, newest first, at most 100.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        public Task<List<RealtimeConnectionRecord>> RecentAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            return _Database.RealtimeConnections.EnumerateRecentByUserAsync(userId, DateTime.UtcNow.AddSeconds(-_OnlineSeconds), token);
        }

        /// <summary>
        /// Insert or update a row (hello and state).
        /// </summary>
        /// <param name="record">Row.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The row is null.</exception>
        public Task UpsertAsync(RealtimeConnectionRecord record, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            return _Database.RealtimeConnections.UpsertAsync(record, token);
        }

        /// <summary>
        /// Touch rows (the 60 second heartbeat of the registry).
        /// </summary>
        /// <param name="ids">Connection ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows touched.</returns>
        /// <exception cref="ArgumentNullException">The ids are null.</exception>
        public Task<int> TouchAsync(IEnumerable<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            return _Database.RealtimeConnections.TouchAsync(ids, DateTime.UtcNow, token);
        }

        /// <summary>
        /// Delete a row (disconnect).
        /// </summary>
        /// <param name="id">Connection id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted.</returns>
        public Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return Task.FromResult(false);
            return _Database.RealtimeConnections.DeleteAsync(id, token);
        }

        #endregion
    }
}
