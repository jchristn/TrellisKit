namespace TrellisKit.Core.Observability
{
    using System;
    using System.Diagnostics.Metrics;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Saves instruments on the TrellisKit meter (REST_API.md, Saves). Labels are low-cardinality only (the target
    /// type, never ids or text). Thread safety: instruments are thread-safe.
    /// </summary>
    public static class SavesTelemetry
    {
        #region Public-Members

        /// <summary>
        /// Saves created, labeled target_type.
        /// </summary>
        public static readonly Counter<long> Created = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.saves.created", "{save}", "Saves created, by target type.");

        /// <summary>
        /// Saves removed (by the member, by target deletion, or by account deletion), labeled target_type.
        /// </summary>
        public static readonly Counter<long> Removed = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.saves.removed", "{save}", "Saves removed, by target type.");

        #endregion

        #region Public-Methods

        /// <summary>
        /// Count created saves.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="amount">Saves.</param>
        public static void CountCreated(FavoriteTargetTypeEnum targetType, long amount = 1)
        {
            if (amount <= 0) return;
            TrellisKitTelemetry.Add(Created, amount, "target_type", targetType.ToString());
        }

        /// <summary>
        /// Count removed saves.
        /// </summary>
        /// <param name="targetType">Target type, or null when the removal spans types (account deletion).</param>
        /// <param name="amount">Saves.</param>
        public static void CountRemoved(FavoriteTargetTypeEnum? targetType, long amount = 1)
        {
            if (amount <= 0) return;
            TrellisKitTelemetry.Add(Removed, amount, "target_type", targetType == null ? "Any" : targetType.Value.ToString());
        }

        #endregion
    }
}
