namespace TrellisKit.Server.Services.Jobs
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Built-in job jobs.prune: deletes finished runs and their items older than jobs.retentionDays, daily.
    /// </summary>
    public sealed class PruneJobHandler : IJobHandler
    {
        #region Public-Members

        /// <inheritdoc />
        public string Key
        {
            get { return "jobs.prune"; }
        }

        /// <inheritdoc />
        public JobHandlerDefaults Defaults { get; } = new JobHandlerDefaults
        {
            Name = "Prune job history",
            Description = "Deletes finished job runs and their items older than the job history retention (jobs.retentionDays).",
            Enabled = true,
            IntervalType = JobIntervalTypeEnum.EveryNDays,
            IntervalValue = 1,
            TimeOfDayMinutesUtc = 11 * 60,
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
            DateTime cutoff = context.NowUtc.AddDays(-context.Services.Settings.Jobs.RetentionDays);
            if (context.IsDryRun) return JobItemOutcome.Ok("{\"dryRun\":true,\"cutoffUtc\":\"" + cutoff.ToString("o", CultureInfo.InvariantCulture) + "\"}");
            int total = 0;
            for (int pass = 0; pass < 20; pass++)
            {
                int deleted = await context.Services.Database.Jobs.PruneAsync(cutoff, 500, token).ConfigureAwait(false);
                total += deleted;
                if (deleted < 500) break;
            }

            return JobItemOutcome.Ok("{\"deletedRuns\":" + total.ToString(CultureInfo.InvariantCulture) + "}");
        }

        #endregion
    }
}
