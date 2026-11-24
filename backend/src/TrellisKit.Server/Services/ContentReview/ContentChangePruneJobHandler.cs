namespace TrellisKit.Server.Services.ContentReview
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Server.Services.Jobs;

    /// <summary>
    /// content.changesPrune: daily, deletes reviewed content changelog rows (Clean, Dismissed, Actioned) older than
    /// contentReview.retentionDays. Pending and Flagged rows are kept until resolved. Enabled by default. Thread safety:
    /// stateless.
    /// </summary>
    public sealed class ContentChangePruneJobHandler : IJobHandler
    {
        #region Public-Members

        /// <inheritdoc />
        public string Key
        {
            get { return ContentReviewService.PruneJobKey; }
        }

        /// <inheritdoc />
        public JobHandlerDefaults Defaults { get; } = new JobHandlerDefaults
        {
            Name = "Prune the content changelog",
            Description = "Deletes reviewed content changelog rows older than the retention (contentReview.retentionDays). Pending and Flagged rows are kept.",
            Enabled = true,
            IntervalType = JobIntervalTypeEnum.EveryNDays,
            IntervalValue = 1,
            TimeOfDayMinutesUtc = 11 * 60 + 30,
            MaxConcurrency = 1,
            MaxAttempts = 3
        };

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<List<JobItemPlan>> PlanAsync(JobRunContext context, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(context);
            return Task.FromResult(new List<JobItemPlan> { new JobItemPlan("prune") });
        }

        /// <inheritdoc />
        public async Task<JobItemOutcome> ExecuteAsync(JobItemContext context, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(context);
            DateTime cutoff = context.NowUtc.AddDays(-context.Services.Settings.ContentReview.RetentionDays);
            if (context.IsDryRun) return JobItemOutcome.Ok("{\"dryRun\":true,\"cutoffUtc\":\"" + cutoff.ToString("o", CultureInfo.InvariantCulture) + "\"}");
            int total = 0;
            for (int pass = 0; pass < 20; pass++)
            {
                int deleted = await context.Services.Database.ContentChanges.PruneAsync(cutoff, 5000, token).ConfigureAwait(false);
                total += deleted;
                if (deleted < 5000) break;
            }
            return JobItemOutcome.Ok("{\"deleted\":" + total.ToString(CultureInfo.InvariantCulture) + "}");
        }

        #endregion
    }
}
