namespace TrellisKit.Server.Services.Jobs
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Built-in job ai.callsPrune: deletes AI call ledger rows older than ai.callRetentionDays, daily.
    /// </summary>
    public sealed class AiCallPruneJobHandler : IJobHandler
    {
        #region Public-Members

        /// <inheritdoc />
        public string Key
        {
            get { return "ai.callsPrune"; }
        }

        /// <inheritdoc />
        public JobHandlerDefaults Defaults { get; } = new JobHandlerDefaults
        {
            Name = "Prune the AI call ledger",
            Description = "Deletes AI call ledger rows older than the call retention (ai.callRetentionDays).",
            Enabled = true,
            IntervalType = JobIntervalTypeEnum.EveryNDays,
            IntervalValue = 1,
            TimeOfDayMinutesUtc = 11 * 60 + 15,
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
            DateTime cutoff = context.NowUtc.AddDays(-context.Services.Settings.Ai.CallRetentionDays);
            if (context.IsDryRun) return JobItemOutcome.Ok("{\"dryRun\":true,\"cutoffUtc\":\"" + cutoff.ToString("o", CultureInfo.InvariantCulture) + "\"}");
            int total = 0;
            for (int pass = 0; pass < 40; pass++)
            {
                int deleted = await context.Services.Database.Ai.Calls.PruneAsync(cutoff, 25000, token).ConfigureAwait(false);
                total += deleted;
                if (deleted < 25000) break;
            }

            return JobItemOutcome.Ok("{\"deletedCalls\":" + total.ToString(CultureInfo.InvariantCulture) + "}");
        }

        #endregion
    }
}
