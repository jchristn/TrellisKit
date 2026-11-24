namespace TrellisKit.Server.Workers
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Ai;

    /// <summary>
    /// Singleton AI housekeeping worker (jobleases lease "ai_maintenance"; docs/AI_CONTRACT.md sections 9.6 and 14.1): fails
    /// replies left Pending or Streaming by a stopped task, deletes conversations idle longer than their assistant's
    /// retention (and every conversation of assistants deleted longer than ai.deletedAssistantRetentionDays), visitor
    /// conversations after ai.visitorRetentionDays, feedback after ai.feedbackRetentionDays, compacts hourly usage rows
    /// older than 30 days into daily rows, and deletes rollups older than ai.analyticsRetentionDays.
    /// Thread safety: one pass at a time per instance; the lease keeps instances from overlapping.
    /// </summary>
    public sealed class AiMaintenanceWorker : IntervalWorker
    {
        #region Public-Members

        /// <summary>
        /// Lease and job name.
        /// </summary>
        public const string Name = "ai_maintenance";

        /// <inheritdoc />
        public override string JobName
        {
            get { return Name; }
        }

        /// <summary>
        /// Identifier of this instance in the lease table.
        /// </summary>
        public string Holder { get; } = "aim-" + Environment.MachineName + "-" + Guid.NewGuid().ToString("N").Substring(0, 12);

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override TimeSpan InitialDelay
        {
            get { return TimeSpan.FromMinutes(3); }
        }

        /// <inheritdoc />
        protected override TimeSpan Interval
        {
            get { return TimeSpan.FromHours(1); }
        }

        #endregion

        #region Private-Members

        private readonly AiServices _Ai;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="ai">AI services of the property.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public AiMaintenanceWorker(AiServices ai, AppLogger logger) : base(logger)
        {
            _Ai = ai ?? throw new ArgumentNullException(nameof(ai));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run one pass without the lease (tests and the worker).
        /// </summary>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows changed.</returns>
        public async Task<int> PassAsync(DateTime nowUtc, CancellationToken token)
        {
            int changed = await _Ai.App.Database.Ai.Conversations.FailStaleRepliesAsync(nowUtc.AddMinutes(-20), token).ConfigureAwait(false);
            string? after = null;
            while (true)
            {
                List<Assistant> page = await _Ai.App.Database.Ai.Assistants.ListPageAsync(200, after, token).ConfigureAwait(false);
                if (page.Count == 0) break;
                foreach (Assistant assistant in page)
                {
                    DateTime cutoff = assistant.DeletedUtc != null && assistant.DeletedUtc < nowUtc.AddDays(-_Ai.App.Settings.Ai.DeletedAssistantRetentionDays)
                        ? nowUtc.AddYears(1)
                        : nowUtc.AddDays(-Math.Max(1, assistant.ConversationRetentionDays));
                    int deleted;
                    do
                    {
                        deleted = await _Ai.App.Database.Ai.Conversations.DeleteIdleConversationsAsync(assistant.Id, cutoff, 500, token).ConfigureAwait(false);
                        changed += deleted;
                    }
                    while (deleted == 500);
                }
                after = page[page.Count - 1].Id;
            }
            int visitors;
            do
            {
                visitors = await _Ai.App.Database.Ai.Conversations.DeleteVisitorConversationsAsync(nowUtc.AddDays(-_Ai.App.Settings.Ai.VisitorRetentionDays), 500, token).ConfigureAwait(false);
                changed += visitors;
            }
            while (visitors == 500);
            changed += await _Ai.App.Database.Ai.Conversations.PruneFeedbackAsync(nowUtc.AddDays(-_Ai.App.Settings.Ai.FeedbackRetentionDays), token).ConfigureAwait(false);
            changed += await _Ai.Reports.PruneAsync(nowUtc, token).ConfigureAwait(false);
            changed += await _Ai.App.Database.Ai.Usage.CompactAsync(nowUtc.Date.AddDays(-30), nowUtc.Date.AddDays(-_Ai.App.Settings.Ai.AnalyticsRetentionDays), token).ConfigureAwait(false);
            return changed;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<int> ExecuteAsync(CancellationToken token)
        {
            if (!_Ai.IsAvailable) return 0;
            DateTime now = DateTime.UtcNow;
            if (!await _Ai.App.Database.JobLeases.TryAcquireAsync(Name, Holder, now, TimeSpan.FromMinutes(30), token).ConfigureAwait(false)) return 0;
            int changed = await PassAsync(now, token).ConfigureAwait(false);
            if (changed > 0) Logger.Info("[AiMaintenanceWorker] changed " + changed + " rows");
            return changed;
        }

        #endregion
    }
}
