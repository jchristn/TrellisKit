namespace TrellisKit.Server.Workers
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Deployment-level worker that purges deleted properties once their restore window
    /// (platform.propertyPurgeDelayHours, 30 days by default) has passed: every property-owned table
    /// WHERE propertyid = id in bounded batches, every stored object under the property prefix, the property's
    /// domains and apps, and finally marks the registry row Deleted (a tombstone; the id is never reused). Also prunes
    /// expired platform administrator sessions. A single instance runs at a time (job lease).
    /// </summary>
    public sealed class PropertyPurgeWorker : IntervalWorker
    {
        #region Public-Members

        /// <summary>
        /// Lease and job name.
        /// </summary>
        public const string Name = "property_purge";

        /// <inheritdoc />
        public override string JobName
        {
            get { return Name; }
        }

        /// <summary>
        /// Rows deleted per statement. Default 1000. Minimum 1, maximum 10000.
        /// </summary>
        public int BatchSize
        {
            get { return _BatchSize; }
            set { _BatchSize = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Time between passes. Default 15 minutes. Minimum 1 minute.
        /// </summary>
        public TimeSpan PassInterval
        {
            get { return _PassInterval; }
            set { _PassInterval = value < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : value; }
        }

        /// <summary>
        /// Clock (tests replace it).
        /// </summary>
        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        /// <summary>
        /// Lease holder identity of this instance.
        /// </summary>
        public string Holder { get; } = "ppw-" + Environment.MachineName + "-" + Guid.NewGuid().ToString("N").Substring(0, 12);

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override TimeSpan Interval
        {
            get { return _PassInterval; }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly PropertyDirectory _Directory;
        private readonly IObjectStore? _Store;
        private readonly string? _StorageDirectory;
        private int _BatchSize = 1000;
        private TimeSpan _PassInterval = TimeSpan.FromMinutes(15);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="database">Deployment driver.</param>
        /// <param name="directory">Property directory (reloaded after a purge).</param>
        /// <param name="store">Object store whose keys are purged, or null.</param>
        /// <param name="storageDirectory">storage.directory (local avatars and files), or null.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public PropertyPurgeWorker(DatabaseDriverBase database, PropertyDirectory directory, IObjectStore? store, string? storageDirectory, AppLogger logger) : base(logger)
        {
            _StorageDirectory = storageDirectory;
            _Database = (database ?? throw new ArgumentNullException(nameof(database))).Deployment;
            _Directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _Store = store;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<int> ExecuteAsync(CancellationToken token)
        {
            DateTime now = Clock();
            if (!await _Database.JobLeases.TryAcquireAsync(Name, Holder, now, TimeSpan.FromHours(1), token).ConfigureAwait(false)) return 0;
            await _Database.PlatformAdmins.PruneSessionsAsync(DateTime.UtcNow.AddDays(-1), token).ConfigureAwait(false);
            int purged = 0;
            foreach (PropertyRecord record in await _Database.Registry.ReadPurgeDueAsync(now, token).ConfigureAwait(false))
            {
                await PurgeAsync(record, token).ConfigureAwait(false);
                purged++;
            }
            if (purged > 0) await _Directory.ReloadAsync(token).ConfigureAwait(false);
            return purged;
        }

        #endregion

        #region Private-Methods

        private async Task PurgeAsync(PropertyRecord record, CancellationToken token)
        {
            Logger.Warn("[PropertyPurgeWorker] purging property " + record.Id + " (" + record.Name + ")");
            record.PurgeStartedUtc ??= Clock();
            record.UpdatedUtc = Clock();
            await _Database.Registry.UpdateAsync(record, token).ConfigureAwait(false);

            long rows = 0;
            foreach (string table in PropertyTables.Owned)
            {
                while (true)
                {
                    int deleted = await _Database.Registry.PurgeBatchAsync(table, record.Id, _BatchSize, token).ConfigureAwait(false);
                    rows += deleted;
                    if (deleted < _BatchSize) break;
                }
            }
            await _Database.ForProperty(PropertyIds.Platform).Metadata.DeleteAsync(TargetTypeEnum.Property, record.Id, token).ConfigureAwait(false);

            int objects = 0;
            if (_Store != null)
            {
                foreach (MediaLocationEnum location in Enum.GetValues<MediaLocationEnum>())
                {
                    try
                    {
                        objects += await _Store.DeletePrefixAsync(location, MediaKeys.Prefix(record.Id), token).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        Logger.Warn("[PropertyPurgeWorker] could not delete " + location + " objects of " + record.Id + ": " + e.Message);
                    }
                }
            }

            if (!String.IsNullOrWhiteSpace(_StorageDirectory))
            {
                // Avatars and other local files live under {storage.directory}/{propertyId}/.
                string folder = System.IO.Path.Combine(System.IO.Path.GetFullPath(_StorageDirectory), MediaKeys.Prefix(record.Id).TrimEnd('/'));
                try
                {
                    if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, true);
                }
                catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
                {
                    Logger.Warn("[PropertyPurgeWorker] could not delete " + folder + ": " + e.Message);
                }
            }

            await _Database.Registry.ReplaceDomainsAndAppsAsync(record.Id, new List<PropertyDomain>(), new List<PropertyApp>(), Clock(), token).ConfigureAwait(false);
            record.Status = PropertyStatusEnum.Deleted;
            record.PurgedUtc = Clock();
            record.UpdatedUtc = record.PurgedUtc.Value;
            await _Database.Registry.UpdateAsync(record, token).ConfigureAwait(false);
            await _Database.ForProperty(PropertyIds.Platform).Audit.CreateAsync(new AuditRecord
            {
                Id = IdGenerator.GenerateAuditRecordId(),
                ActorName = "system",
                Action = "platform.property.purge",
                TargetType = "Property",
                TargetId = record.Id,
                Summary = "Purged " + rows + " rows and " + objects + " objects",
                CreatedUtc = Clock()
            }, token).ConfigureAwait(false);
            Logger.Warn("[PropertyPurgeWorker] purged property " + record.Id + ": " + rows + " rows, " + objects + " objects");
        }

        #endregion
    }
}
