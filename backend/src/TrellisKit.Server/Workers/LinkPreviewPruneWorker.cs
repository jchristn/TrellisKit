namespace TrellisKit.Server.Workers
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Singleton prune worker (jobleases lease "link_preview_prune"): deletes expired Failed link previews and expired
    /// Ok previews that no feed post or comment references, and word filter hits older than <see cref="HitRetentionDays"/>.
    /// Thread safety: one pass at a time per instance; the lease keeps instances from overlapping.
    /// </summary>
    public sealed class LinkPreviewPruneWorker : IntervalWorker
    {
        #region Public-Members

        /// <summary>
        /// Lease and job name.
        /// </summary>
        public const string Name = "link_preview_prune";

        /// <inheritdoc />
        public override string JobName
        {
            get { return Name; }
        }

        /// <summary>
        /// Word filter hit retention in days. Default 90, minimum 1, maximum 3650.
        /// </summary>
        public int HitRetentionDays
        {
            get { return _HitRetentionDays; }
            set { _HitRetentionDays = Math.Clamp(value, 1, 3650); }
        }

        /// <summary>
        /// Time between passes. Default 1 hour, minimum 1 minute.
        /// </summary>
        public TimeSpan PassInterval
        {
            get { return _PassInterval; }
            set { _PassInterval = value < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : value; }
        }

        /// <summary>
        /// Identifier of this instance in the lease table.
        /// </summary>
        public string Holder { get; } = "lpp-" + Environment.MachineName + "-" + Guid.NewGuid().ToString("N").Substring(0, 12);

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
        private int _HitRetentionDays = 90;
        private TimeSpan _PassInterval = TimeSpan.FromHours(1);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public LinkPreviewPruneWorker(DatabaseDriverBase database, AppLogger logger) : base(logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<int> ExecuteAsync(CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            if (!await _Database.JobLeases.TryAcquireAsync(Name, Holder, now, TimeSpan.FromMinutes(10), token).ConfigureAwait(false)) return 0;
            int previews = await _Database.LinkPreviews.PruneAsync(now, token).ConfigureAwait(false);
            int hits = await _Database.WordFilter.PruneHitsAsync(now.AddDays(-_HitRetentionDays), token).ConfigureAwait(false);
            if (previews + hits > 0) Logger.Info("[LinkPreviewPruneWorker] pruned " + previews + " link previews and " + hits + " word filter hits");
            return previews + hits;
        }

        #endregion
    }
}
