namespace TrellisKit.Server.Workers
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Nightly reconciliation: re-queries every subscription that is not Expired, Revoked, or Refunded from its store
    /// (least recently checked first) so missed webhooks cannot leave a membership wrong for long.
    /// </summary>
    public sealed class SubscriptionReconciliationWorker : IntervalWorker
    {
        #region Public-Members

        /// <inheritdoc />
        public override string JobName
        {
            get { return "subscription_reconcile"; }
        }

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override TimeSpan InitialDelay
        {
            get { return TimeSpan.FromMinutes(10); }
        }

        /// <inheritdoc />
        protected override TimeSpan Interval
        {
            get { return TimeSpan.FromMinutes(_Settings.ReconcileIntervalMinutes); }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly PurchaseService _Purchases;
        private readonly MembershipSettings _Settings;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="purchases">Purchase service.</param>
        /// <param name="settings">Membership settings (live).</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public SubscriptionReconciliationWorker(DatabaseDriverBase database, PurchaseService purchases, MembershipSettings settings, AppLogger logger) : base(logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Purchases = purchases ?? throw new ArgumentNullException(nameof(purchases));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<int> ExecuteAsync(CancellationToken token)
        {
            List<Subscription> due = await _Database.Subscriptions.EnumerateForReconciliationAsync(_Settings.ReconcileBatchSize, token).ConfigureAwait(false);
            int checkedCount = 0;
            int failed = 0;
            foreach (Subscription subscription in due)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (await _Purchases.ReconcileAsync(subscription, token).ConfigureAwait(false)) checkedCount++;
                }
                catch (Exception e) when (e is StoreVerificationException || e is IntegrationNotConfiguredException || e is System.Net.Http.HttpRequestException)
                {
                    failed++;
                    Logger.Warn("[SubscriptionReconciliationWorker] " + subscription.Store + " subscription " + subscription.Id + " could not be reconciled: " + e.Message);
                }
            }
            if (due.Count > 0) Logger.Info("[SubscriptionReconciliationWorker] reconciled " + checkedCount + " of " + due.Count + " subscriptions (" + failed + " failed)");
            return checkedCount;
        }

        #endregion
    }
}
