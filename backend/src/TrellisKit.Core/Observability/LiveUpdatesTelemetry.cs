namespace TrellisKit.Core.Observability
{
    using System;
    using System.Diagnostics.Metrics;

    /// <summary>
    /// Live update and app configuration instruments on the TrellisKit meter (docs/LIVE_UPDATES_CONTRACT.md section 20).
    /// Labels are low cardinality: never update ids, install ids, or flag keys of ordinary flags.
    /// </summary>
    public static class LiveUpdatesTelemetry
    {
        #region Public-Members

        /// <summary>
        /// trelliskit.updates.checks (platform, channel, outcome, protocol).
        /// </summary>
        public static readonly Counter<long> Checks = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.updates.checks", description: "Live update manifest requests by outcome.");

        /// <summary>
        /// trelliskit.updates.check.duration in seconds (platform).
        /// </summary>
        public static readonly Histogram<double> CheckDuration = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.updates.check.duration", unit: "s", description: "Live update manifest request duration.");

        /// <summary>
        /// trelliskit.updates.downloads (platform, channel).
        /// </summary>
        public static readonly Counter<long> Downloads = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.updates.downloads", description: "Client-reported update downloads.");

        /// <summary>
        /// trelliskit.updates.applied (platform, channel).
        /// </summary>
        public static readonly Counter<long> Applied = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.updates.applied", description: "Client-reported update launches.");

        /// <summary>
        /// trelliskit.updates.failures (platform, channel, stage, error_code).
        /// </summary>
        public static readonly Counter<long> Failures = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.updates.failures", description: "Update check, download, and launch failures.");

        /// <summary>
        /// trelliskit.updates.signing (outcome).
        /// </summary>
        public static readonly Counter<long> Signing = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.updates.signing", description: "Manifest and directive signatures by outcome.");

        /// <summary>
        /// trelliskit.updates.publishes (action, credential).
        /// </summary>
        public static readonly Counter<long> Publishes = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.updates.publishes", description: "Update lifecycle actions.");

        /// <summary>
        /// trelliskit.updates.asset.bytes (direction).
        /// </summary>
        public static readonly Counter<long> AssetBytes = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.updates.asset.bytes", unit: "By", description: "Update asset bytes uploaded and served.");

        /// <summary>
        /// trelliskit.updates.auto_halts (channel).
        /// </summary>
        public static readonly Counter<long> AutoHalts = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.updates.auto_halts", description: "Rollouts paused by the automatic failure halt.");

        /// <summary>
        /// trelliskit.app.manifest.requests (client, outcome).
        /// </summary>
        public static readonly Counter<long> AppManifestRequests = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.app.manifest.requests", description: "App content manifest requests.");

        /// <summary>
        /// trelliskit.app.manifest.duration in seconds (client).
        /// </summary>
        public static readonly Histogram<double> AppManifestDuration = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.app.manifest.duration", unit: "s", description: "App content manifest duration.");

        /// <summary>
        /// trelliskit.app.content.publishes (action).
        /// </summary>
        public static readonly Counter<long> ContentPublishes = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.app.content.publishes", description: "App content publishes, restores, and discards.");

        /// <summary>
        /// trelliskit.killswitch.changes (feature, action).
        /// </summary>
        public static readonly Counter<long> KillSwitchChanges = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.killswitch.changes", description: "Kill switch changes.");

        /// <summary>
        /// trelliskit.killswitch.blocked (feature, client).
        /// </summary>
        public static readonly Counter<long> KillSwitchBlocked = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.killswitch.blocked", description: "API requests blocked by a kill switch.");

        /// <summary>
        /// trelliskit.flags.evaluations (surface, reason).
        /// </summary>
        public static readonly Counter<long> FlagEvaluations = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.flags.evaluations", description: "Feature flag evaluations.");

        /// <summary>
        /// trelliskit.updates.gc.deleted (kind: asset): unreferenced update assets UpdateAssetGcWorker deleted.
        /// </summary>
        public static readonly Counter<long> GcDeleted = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.updates.gc.deleted", description: "Unreferenced update assets deleted by garbage collection.");

        /// <summary>
        /// trelliskit.updates.gc.bytes (kind: asset): bytes UpdateAssetGcWorker freed.
        /// </summary>
        public static readonly Counter<long> GcBytes = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.updates.gc.bytes", unit: "By", description: "Bytes freed by update asset garbage collection.");

        /// <summary>
        /// trelliskit.updates.gc.runs (outcome: completed, failed, skipped).
        /// </summary>
        public static readonly Counter<long> GcRuns = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.updates.gc.runs", description: "Update asset garbage collection passes by outcome.");

        /// <summary>
        /// trelliskit.updates.symbolications (outcome: symbolicated, no_source_map).
        /// </summary>
        public static readonly Counter<long> Symbolications = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.updates.symbolications", description: "Admin stack trace symbolications by outcome.");

        #endregion
    }
}
