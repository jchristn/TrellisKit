namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Lifecycle status of a live update group (docs/LIVE_UPDATES_CONTRACT.md section 10.4).
    /// </summary>
    public enum UpdateGroupStatusEnum
    {
        /// <summary>
        /// Uploaded, never offered to installs.
        /// </summary>
        Draft,

        /// <summary>
        /// Offered to installs inside its rollout.
        /// </summary>
        Published,

        /// <summary>
        /// No longer offered to installs that do not run it; installs on it keep it.
        /// </summary>
        Paused,

        /// <summary>
        /// Replaced by a rollback; never offered again.
        /// </summary>
        RolledBack,

        /// <summary>
        /// A newer group reached 100 percent on every target, or a newer publish superseded its rollout.
        /// </summary>
        Superseded
    }
}
