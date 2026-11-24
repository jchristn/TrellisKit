namespace TrellisKit.Server.Workers
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Deletes update assets (bundles and assets content addressed under updates/assets/{propertyId}/...) that no update
    /// references and that were last referenced more than updates.assetGcRetentionDays ago (docs/LIVE_UPDATES_CONTRACT.md
    /// section 17.1). One pass per updates.assetGcIntervalHours (weekly by default) for this property, under a job lease,
    /// in batches of updates.assetGcBatchSize: each batch removes the rows in one statement that re-checks references,
    /// then deletes the stored objects, so an asset a new upload references again is never removed. Metrics
    /// trelliskit.updates.gc.deleted, .bytes, and .runs.
    /// </summary>
    public sealed class UpdateAssetGcWorker : IntervalWorker
    {
        #region Public-Members

        /// <summary>
        /// Job name.
        /// </summary>
        public const string Name = "update_asset_gc";

        /// <summary>
        /// Most batches per pass (bounds one pass; the next pass continues).
        /// </summary>
        public const int MaxBatchesPerPass = 50;

        /// <inheritdoc />
        public override string JobName
        {
            get { return Name; }
        }

        /// <summary>
        /// Lease holder id of this task.
        /// </summary>
        public string Holder { get; } = "uag-" + Environment.MachineName + "-" + Guid.NewGuid().ToString("N").Substring(0, 12);

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override TimeSpan Interval
        {
            get { return TimeSpan.FromHours(_Settings.AssetGcIntervalHours); }
        }

        /// <inheritdoc />
        protected override TimeSpan InitialDelay
        {
            get { return TimeSpan.FromMinutes(5); }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly UpdatesSettings _Settings;
        private readonly UpdateObjectStore _Store;
        private readonly string _PropertyId;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="database">Property-bound database driver.</param>
        /// <param name="settings">Updates settings.</param>
        /// <param name="store">Update storage.</param>
        /// <param name="propertyId">Property id.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public UpdateAssetGcWorker(DatabaseDriverBase database, UpdatesSettings settings, UpdateObjectStore store, string propertyId, AppLogger logger) : base(logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Store = store ?? throw new ArgumentNullException(nameof(store));
            _PropertyId = propertyId ?? throw new ArgumentNullException(nameof(propertyId));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run one pass now (tests and operators). Returns the number of assets deleted; 0 when another task holds the lease.
        /// </summary>
        /// <param name="nowUtc">Clock.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Assets deleted.</returns>
        public async Task<int> CollectAsync(DateTime nowUtc, CancellationToken token = default)
        {
            if (!await _Database.JobLeases.TryAcquireAsync(Name, Holder, nowUtc, TimeSpan.FromMinutes(30), token).ConfigureAwait(false))
            {
                TrellisKitTelemetry.Add(LiveUpdatesTelemetry.GcRuns, 1, "outcome", "skipped", "trelliskit.property", _PropertyId);
                return 0;
            }
            int deleted = 0;
            long bytes = 0;
            try
            {
                DateTime cutoff = nowUtc.AddDays(-_Settings.AssetGcRetentionDays);
                for (int batch = 0; batch < MaxBatchesPerPass; batch++)
                {
                    List<UpdateAsset> rows = await _Database.LiveUpdates.DeleteUnreferencedAssetsAsync(cutoff, _Settings.AssetGcBatchSize, token).ConfigureAwait(false);
                    foreach (UpdateAsset asset in rows)
                    {
                        try
                        {
                            await _Store.DeleteAsync(UpdateObjectStore.AssetKey(_PropertyId, asset.Sha256Hex), token).ConfigureAwait(false);
                        }
                        catch (Exception e) when (e is not OperationCanceledException)
                        {
                            Logger.Warn("[UpdateAssetGcWorker] [p:" + _PropertyId + "] could not delete object of asset " + asset.Sha256Hex + ": " + e.Message);
                        }
                        deleted++;
                        bytes += asset.SizeBytes;
                    }
                    if (rows.Count < _Settings.AssetGcBatchSize) break;
                }
                deleted += await CollectObjectsAsync(UpdateGcObject.SourceMapKind, "sourcemap", cutoff, nowUtc, token).ConfigureAwait(false);
                deleted += await CollectObjectsAsync(UpdateGcObject.ContentAssetKind, "content", cutoff, nowUtc, token).ConfigureAwait(false);
                TrellisKitTelemetry.Add(LiveUpdatesTelemetry.GcRuns, 1, "outcome", "completed", "trelliskit.property", _PropertyId);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                TrellisKitTelemetry.Add(LiveUpdatesTelemetry.GcRuns, 1, "outcome", "failed", "trelliskit.property", _PropertyId);
                throw;
            }
            finally
            {
                if (deleted > 0)
                {
                    TrellisKitTelemetry.Add(LiveUpdatesTelemetry.GcDeleted, deleted, "kind", "asset", "trelliskit.property", _PropertyId);
                    TrellisKitTelemetry.Add(LiveUpdatesTelemetry.GcBytes, bytes, "kind", "asset", "trelliskit.property", _PropertyId);
                    Logger.Info("[UpdateAssetGcWorker] [p:" + _PropertyId + "] deleted " + deleted + " unreferenced update assets (" + bytes + " bytes)");
                }
                await _Database.JobLeases.ReleaseAsync(Name, Holder, CancellationToken.None).ConfigureAwait(false);
            }
            return deleted;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task<int> ExecuteAsync(CancellationToken token)
        {
            return CollectAsync(DateTime.UtcNow, token);
        }

        #endregion

        #region Private-Methods

        private async Task<int> CollectObjectsAsync(string kind, string label, DateTime cutoff, DateTime nowUtc, CancellationToken token)
        {
            // Private source maps of deleted draft updates and content asset images no remote asset revision references,
            // in the same bounded batches as update assets (at most MaxBatchesPerPass batches per kind).
            int deleted = 0;
            long bytes = 0;
            for (int batch = 0; batch < MaxBatchesPerPass; batch++)
            {
                List<UpdateGcObject> rows = await _Database.LiveUpdates.DeleteUnreferencedGcObjectsAsync(kind, cutoff, nowUtc, _Settings.AssetGcBatchSize, token).ConfigureAwait(false);
                foreach (UpdateGcObject row in rows)
                {
                    try
                    {
                        await _Store.DeleteAsync(row.StorageKey, token).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        Logger.Warn("[UpdateAssetGcWorker] [p:" + _PropertyId + "] could not delete " + label + " object " + row.Sha256Hex + ": " + e.Message);
                    }
                    deleted++;
                    bytes += row.SizeBytes;
                }
                if (rows.Count < _Settings.AssetGcBatchSize) break;
            }
            if (deleted > 0)
            {
                TrellisKitTelemetry.Add(LiveUpdatesTelemetry.GcDeleted, deleted, "kind", label, "trelliskit.property", _PropertyId);
                TrellisKitTelemetry.Add(LiveUpdatesTelemetry.GcBytes, bytes, "kind", label, "trelliskit.property", _PropertyId);
                Logger.Info("[UpdateAssetGcWorker] [p:" + _PropertyId + "] deleted " + deleted + " unreferenced " + label + " objects (" + bytes + " bytes)");
            }
            return deleted;
        }

        #endregion
    }
}
