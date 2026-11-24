namespace TrellisKit.Core.Observability
{
    using System;
    using System.Diagnostics.Metrics;

    /// <summary>
    /// Phase 5 ideas board instruments on the TrellisKit meter (docs/PHASE5_CONTRACT.md section 15). Labels are
    /// low-cardinality only (never ids or text). Thread safety: instruments are thread-safe.
    /// </summary>
    public static class IdeaTelemetry
    {
        #region Public-Members

        /// <summary>
        /// Ideas board actions, labeled action (create, vote, unvote, status, merge, unmerge, delete, update, follow, unfollow).
        /// </summary>
        public static readonly Counter<long> Actions = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.ideas.actions", "{action}", "Ideas board actions.");

        #endregion

        #region Public-Methods

        /// <summary>
        /// Count one action.
        /// </summary>
        /// <param name="action">Action label.</param>
        /// <exception cref="ArgumentNullException">The action is null or empty.</exception>
        public static void Count(string action)
        {
            if (String.IsNullOrEmpty(action)) throw new ArgumentNullException(nameof(action));
            TrellisKitTelemetry.Add(Actions, 1, "action", action);
        }

        #endregion
    }
}
