namespace TrellisKit.Server.Workers
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Deletes request history older than the configured retention, on the configured interval. Both values are
    /// read live on each pass.
    /// </summary>
    public sealed class RequestHistoryPruneWorker : IAsyncDisposable
    {
        #region Public-Members

        /// <summary>
        /// Job name used in telemetry.
        /// </summary>
        public const string JobName = "request_history_prune";

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly RequestHistorySettings _Settings;
        private readonly AppLogger _Logger;
        private CancellationTokenSource? _Source = null;
        private Task? _Loop = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Request history settings (the running instance).</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public RequestHistoryPruneWorker(DatabaseDriverBase database, RequestHistorySettings settings, AppLogger logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the worker loop. The first pass runs immediately.
        /// </summary>
        /// <param name="token">Token that stops the loop when cancelled.</param>
        /// <exception cref="InvalidOperationException">The worker is already running.</exception>
        public void Start(CancellationToken token)
        {
            if (_Loop != null) throw new InvalidOperationException("The prune worker is already running.");
            _Source = CancellationTokenSource.CreateLinkedTokenSource(token);
            _Loop = Task.Run(() => RunAsync(_Source.Token));
        }

        /// <summary>
        /// Run one pruning pass.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        public async Task<int> RunOnceAsync(CancellationToken token = default)
        {
            using (Activity? activity = TrellisKitTelemetry.ActivitySource.StartActivity("job " + JobName))
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                try
                {
                    DateTime cutoff = DateTime.UtcNow.AddDays(-_Settings.RetentionDays);
                    // Request history is property-owned (including the platform and unresolved pseudo-ids): a bound
                    // driver prunes its property; the deployment driver prunes every registered property in turn.
                    List<string> propertyIds = new List<string>();
                    if (_Database.BoundPropertyId != null) propertyIds.Add(_Database.BoundPropertyId);
                    else
                    {
                        propertyIds.AddRange((await _Database.Registry.ReadLiveAsync(token).ConfigureAwait(false)).Select(p => p.Id));
                        propertyIds.Add(PropertyIds.Platform);
                        propertyIds.Add(PropertyIds.Unresolved);
                    }
                    int deleted = 0;
                    foreach (string propertyId in propertyIds)
                        deleted += await _Database.ForProperty(propertyId).RequestHistory.PruneAsync(cutoff, token).ConfigureAwait(false);
                    TrellisKitTelemetry.Add(TrellisKitTelemetry.RequestHistoryPruned, deleted);
                    TrellisKitTelemetry.RecordJob(JobName, stopwatch.Elapsed, true);
                    activity?.SetTag("deleted", deleted);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    if (deleted > 0) _Logger.Info("[RequestHistoryPruneWorker] pruned " + deleted + " entries older than " + _Settings.RetentionDays + " days");
                    return deleted;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    TrellisKitTelemetry.RecordJob(JobName, stopwatch.Elapsed, false);
                    activity?.SetStatus(ActivityStatusCode.Error, e.GetType().Name);
                    throw;
                }
            }
        }

        /// <summary>
        /// Stop the worker and wait for the loop to finish.
        /// </summary>
        /// <returns>Task.</returns>
        public async ValueTask DisposeAsync()
        {
            if (_Source == null) return;
            _Source.Cancel();
            if (_Loop != null)
            {
                try
                {
                    await _Loop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected on shutdown.
                }
            }
            _Source.Dispose();
            _Source = null;
            _Loop = null;
        }

        #endregion

        #region Private-Methods

        private async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    _Logger.Error("[RequestHistoryPruneWorker] pass failed", e);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(_Settings.PruneIntervalMinutes), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        #endregion
    }
}
