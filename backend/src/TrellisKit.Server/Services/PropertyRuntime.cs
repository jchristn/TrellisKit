namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Workers;

    /// <summary>
    /// One registered property at run time: its registry record, configuration, domains and apps, the application
    /// service graph bound to it (property-bound database, caches, rate limits, integrations), and its background
    /// workers (each worker pass touches only this property; job leases are per property).
    /// Thread safety: the record, domains, and apps are replaced as whole objects on registry reload; safe to read
    /// concurrently.
    /// </summary>
    public sealed class PropertyRuntime : IAsyncDisposable
    {
        #region Public-Members

        /// <summary>
        /// Property id.
        /// </summary>
        public string Id
        {
            get { return Configuration.Id; }
        }

        /// <summary>
        /// Registry record (status and lifecycle timestamps).
        /// </summary>
        public PropertyRecord Record
        {
            get { return _Record; }
            internal set { _Record = value ?? throw new ArgumentNullException(nameof(Record)); }
        }

        /// <summary>
        /// Property configuration.
        /// </summary>
        public PropertyConfiguration Configuration { get; }

        /// <summary>
        /// Service graph bound to this property.
        /// </summary>
        public ApplicationServices Services { get; }

        /// <summary>
        /// Registered domains.
        /// </summary>
        public IReadOnlyList<PropertyDomain> Domains
        {
            get { return _Domains; }
            internal set { _Domains = value ?? new List<PropertyDomain>(); }
        }

        /// <summary>
        /// Registered apps.
        /// </summary>
        public IReadOnlyList<PropertyApp> Apps
        {
            get { return _Apps; }
            internal set { _Apps = value ?? new List<PropertyApp>(); }
        }

        /// <summary>
        /// Whether the property serves requests.
        /// </summary>
        public bool IsEnabled
        {
            get { return _Record.Status == PropertyStatusEnum.Enabled; }
        }

        /// <summary>
        /// Whether background workers are running.
        /// </summary>
        public bool WorkersRunning
        {
            get { return _Workers.Count > 0; }
        }

        #endregion

        #region Private-Members

        private readonly AppSettings _Settings;
        private readonly AppLogger _Logger;
        private readonly List<IntervalWorker> _Workers = new List<IntervalWorker>();
        private PropertyRecord _Record;
        private IReadOnlyList<PropertyDomain> _Domains = new List<PropertyDomain>();
        private IReadOnlyList<PropertyApp> _Apps = new List<PropertyApp>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a runtime.
        /// </summary>
        /// <param name="record">Registry record.</param>
        /// <param name="configuration">Property configuration.</param>
        /// <param name="services">Service graph bound to the property.</param>
        /// <param name="settings">Running settings.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public PropertyRuntime(PropertyRecord record, PropertyConfiguration configuration, ApplicationServices services, AppSettings settings, AppLogger logger)
        {
            _Record = record ?? throw new ArgumentNullException(nameof(record));
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            Services = services ?? throw new ArgumentNullException(nameof(services));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create and start this property's background workers (idempotent).
        /// </summary>
        /// <param name="token">Lifetime token.</param>
        public void StartWorkers(CancellationToken token)
        {
            if (_Workers.Count > 0) return;
            ApplicationServices s = Services;
            FeedPublishWorker feedPublish = new FeedPublishWorker(s.Database, s.Feed, _Settings, _Logger);
            NotificationFanoutWorker fanout = new NotificationFanoutWorker(s.Database, s.Notifications, s.FeedPostEmails, s.Summaries, _Settings, _Logger) { Access = () => s.Access.Current };
            SignupCodeReminderWorker reminder = new SignupCodeReminderWorker(s.Database, s.Membership, s.Email, _Settings.SignupCodes, _Settings.Membership, _Logger) { Notifications = s.Notifications, Access = () => s.Access.Current };
            _Workers.Add(new SubscriptionReconciliationWorker(s.Database, s.Purchases, _Settings.Membership, _Logger));
            _Workers.Add(reminder);
            _Workers.Add(new LinkPreviewPruneWorker(s.Database, _Logger));
            _Workers.Add(new DisplayNameWordsBackfillWorker(s.MemberSearch, _Logger));
            _Workers.Add(new ExcerptRefreshWorker(s.Database, _Logger));
            _Workers.Add(fanout);
            _Workers.Add(new PushDeliveryWorker(s.Database, s.Integrations.Push, _Settings, _Logger) { PreferenceExemptions = s.Broadcasts.PushExemptionsAsync });
            _Workers.Add(new NotificationPruneWorker(s.Database, _Settings.Notifications, _Logger));
            _Workers.Add(new MediaProcessingWorker(s.Database, s.Media, _Settings.Media, _Logger));
            _Workers.Add(new MediaCleanupWorker(s.Database, s.Media, _Settings.Media, _Logger));
            _Workers.Add(new AccountExportWorker(s.AccountExports, _Logger));
            _Workers.Add(feedPublish);
            _Workers.Add(new ModerationDueWorker(s.Database, s.Moderation, _Logger));
            _Workers.Add(new ContentPurgeWorker(s.Database, s.Moderation, _Logger));
            _Workers.Add(new EmailDeliveryWorker(s.Email, _Settings.Email, _Logger));
            _Workers.Add(new UpdateRolloutWorker(s.Database, s.LiveUpdates.Publish, _Settings.Updates, _Logger));
            _Workers.Add(new UpdatePruneWorker(s.Database, _Settings.Updates, _Logger));
            _Workers.Add(new UpdateAssetGcWorker(s.Database, _Settings.Updates, s.LiveUpdates.Store, s.PropertyId, _Logger));
            _Workers.Add(new SecretRotationWorker(s.SecretRotation, _Settings, _Logger));
            LeasedPassWorker threadPublish = new LeasedPassWorker(s.Database, "forum_thread_publish", () => TimeSpan.FromSeconds(Math.Clamp(_Settings.Feed.PublishIntervalSeconds, 5, 600)), TimeSpan.FromSeconds(10),
                async ct =>
                {
                    int published = await s.Forums.Threads.PublishDueAsync(100, ct).ConfigureAwait(false);
                    await s.Forums.Threads.RefreshReviewGaugeAsync(ct).ConfigureAwait(false);
                    return published;
                }, _Logger);
            LeasedPassWorker trending = new LeasedPassWorker(s.Database, "forum_trending", () => TimeSpan.FromMinutes(Math.Clamp(_Settings.Trending.IntervalMinutes, 1, 1440)), TimeSpan.FromSeconds(30),
                ct => _Settings.Trending.Enabled ? s.Forums.Trending.RecomputeAsync(ct) : Task.FromResult(0), _Logger) { LeaseDuration = TimeSpan.FromMinutes(10) };
            LeasedPassWorker pollClose = new LeasedPassWorker(s.Database, "forum_poll_close", () => TimeSpan.FromSeconds(Math.Clamp(_Settings.Polls.CloseIntervalSeconds, 5, 3600)), TimeSpan.FromSeconds(15),
                ct => s.Forums.Polls.CloseDueAsync(100, ct), _Logger);
            LeasedPassWorker readPrune = new LeasedPassWorker(s.Database, "forum_read_prune", () => TimeSpan.FromHours(1), TimeSpan.FromMinutes(5),
                ct => s.Database.Threads.PruneReadsAsync(DateTime.UtcNow.AddDays(-Math.Max(1, _Settings.Forums.ReadStateRetentionDays)), 5000, ct), _Logger);
            LeasedPassWorker emailDigest = new LeasedPassWorker(s.Database, "forum_email_digest", () => TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(2),
                ct => s.Forums.Digests.RunAsync(DateTime.UtcNow, 500, ct), _Logger) { LeaseDuration = TimeSpan.FromMinutes(10) };
            LeasedPassWorker searchIndex = new LeasedPassWorker(s.Database, "search_index", () => TimeSpan.FromSeconds(Math.Clamp(_Settings.Search.IndexIntervalSeconds, 1, 3600)), TimeSpan.FromSeconds(5),
                async ct =>
                {
                    if (!_Settings.Search.Enabled) return 0;
                    await s.Forums.Search.SweepAsync(ct).ConfigureAwait(false);
                    return await s.Forums.Search.DrainAsync(_Settings.Search.IndexBatchSize, ct).ConfigureAwait(false);
                }, _Logger) { LeaseDuration = TimeSpan.FromMinutes(5) };
            _Workers.Add(threadPublish);
            _Workers.Add(trending);
            _Workers.Add(pollClose);
            _Workers.Add(readPrune);
            _Workers.Add(emailDigest);
            _Workers.Add(searchIndex);
            LeasedPassWorker ideaTrending = new LeasedPassWorker(s.Database, "idea_trending", () => TimeSpan.FromMinutes(Math.Clamp(_Settings.Ideas.TrendingIntervalMinutes, 5, 1440)), TimeSpan.FromSeconds(45),
                ct => s.Ideas.RecomputeTrendingAsync(ct), _Logger) { LeaseDuration = TimeSpan.FromMinutes(10) };
            _Workers.Add(ideaTrending);
            _Workers.Add(new LeasedPassWorker(s.Database, "support_ticket_due", () => TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(20),
                ct => s.Support.Workflow.RunDueAsync(s.Support.Clock(), ct), _Logger));
            _Workers.Add(new LeasedPassWorker(s.Database, "support_ticket_autoclose", () => TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(1),
                ct => s.Support.Workflow.RunAutoCloseAsync(s.Support.Clock(), ct), _Logger));
            // Direct messaging workers (docs/PHASE5_CONTRACT.md sections 3.6, 4.9, and as-built note B).
            _Workers.Add(new LeasedPassWorker(s.Database, "message_email", () => TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1), ct => s.Messaging.Maintenance.EmailPassAsync(ct), _Logger) { LeaseDuration = TimeSpan.FromMinutes(10) });
            _Workers.Add(new LeasedPassWorker(s.Database, "message_retention", () => TimeSpan.FromHours(1), TimeSpan.FromMinutes(3), ct => s.Messaging.Maintenance.RetentionPassAsync(ct), _Logger) { LeaseDuration = TimeSpan.FromMinutes(10) });
            _Workers.Add(new LeasedPassWorker(s.Database, "conversation_purge", () => TimeSpan.FromHours(1), TimeSpan.FromMinutes(4), ct => s.Messaging.Maintenance.PurgePassAsync(ct), _Logger) { LeaseDuration = TimeSpan.FromMinutes(10) });
            _Workers.Add(new LeasedPassWorker(s.Database, "messaging_lapse", () => TimeSpan.FromHours(1), TimeSpan.FromMinutes(5), ct => s.Messaging.Maintenance.LapsePassAsync(ct), _Logger) { LeaseDuration = TimeSpan.FromMinutes(10) });
            _Workers.Add(new ModelEndpointHealthWorker(s.Ai, _Logger));
            _Workers.Add(new AiMaintenanceWorker(s.Ai, _Logger));
            _Workers.Add(new KnowledgeIndexWorker(s.Ai, _Logger));
            _Workers.Add(new UsageWorker(s.Database, s.Usage, _Settings.Usage, _Logger));
            _Workers.Add(new JobWorker(s.Jobs, _Settings.Jobs, _Logger));
            _Workers.Add(new MetricsWorker(s.Metrics, _Logger));
            s.Library.AddWorkers(_Workers, token);
            s.Events.AddWorkers(_Workers, token);
            s.Directory.AddWorkers(_Workers, token);
            s.Feed.PublishSignal = () => feedPublish.Signal(token);
            s.Notifications.FanoutSignal = () => fanout.Signal(token);
            s.Forums.Search.Signal = () => searchIndex.Signal(token);
            BroadcastSendWorker broadcastSend = new BroadcastSendWorker(s.Broadcasts.Sender, _Logger);
            _Workers.Add(broadcastSend);
            s.Broadcasts.Broadcasts.SendSignal = () => broadcastSend.Signal(token);
            // Each worker task captures the execution context when it starts, so its metrics carry this property.
            string? previous = TrellisKitTelemetry.CurrentProperty;
            TrellisKitTelemetry.CurrentProperty = Id;
            foreach (IntervalWorker worker in _Workers)
            {
                worker.RecordPass = s.Metrics.RecordWorkerAsync;
                worker.Start(token);
            }
            TrellisKitTelemetry.CurrentProperty = previous;
            _Logger.Info("[PropertyRuntime] [p:" + Id + "] started " + _Workers.Count + " workers");
        }

        /// <summary>
        /// Stop this property's background workers.
        /// </summary>
        /// <returns>Task.</returns>
        public async Task StopWorkersAsync()
        {
            foreach (IntervalWorker worker in _Workers) await worker.DisposeAsync().ConfigureAwait(false);
            _Workers.Clear();
            // A property being deleted or purged keeps no usage: its pending counters are dropped, never written back.
            try
            {
                if (_Record.Status != PropertyStatusEnum.Deleting && _Record.Status != PropertyStatusEnum.Deleted) await Services.Usage.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _Logger.Warn("[PropertyRuntime] [p:" + Id + "] usage counters were not flushed at shutdown: " + e.Message);
            }
            Services.Feed.PublishSignal = null;
            Services.Notifications.FanoutSignal = null;
            Services.Forums.Search.Signal = null;
        }

        /// <summary>
        /// Stop the workers.
        /// </summary>
        /// <returns>Task.</returns>
        public async ValueTask DisposeAsync()
        {
            await StopWorkersAsync().ConfigureAwait(false);
            await Services.Forums.DisposeAsync().ConfigureAwait(false);
        }

        #endregion
    }
}
