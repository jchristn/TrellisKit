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
    /// content.reviewDigest: daily at 15:00 UTC, emails administrators a digest of the contributions flagged in the last
    /// 24 hours (nothing when none were). Enabled by default. Thread safety: stateless.
    /// </summary>
    public sealed class ContentReviewDigestJobHandler : IJobHandler
    {
        #region Public-Members

        /// <inheritdoc />
        public string Key
        {
            get { return ContentReviewService.DigestJobKey; }
        }

        /// <inheritdoc />
        public JobHandlerDefaults Defaults { get; } = new JobHandlerDefaults
        {
            Name = "Content review digest",
            Description = "Emails administrators a daily digest of the member contributions the content reviewer flagged.",
            Enabled = true,
            IntervalType = JobIntervalTypeEnum.EveryNDays,
            IntervalValue = 1,
            TimeOfDayMinutesUtc = 15 * 60,
            MaxConcurrency = 1,
            MaxAttempts = 3,
            SupportsDryRun = false
        };

        #endregion

        #region Private-Members

        private readonly ContentReviewService _Review;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="review">Content review service.</param>
        /// <exception cref="ArgumentNullException">review is null.</exception>
        public ContentReviewDigestJobHandler(ContentReviewService review)
        {
            _Review = review ?? throw new ArgumentNullException(nameof(review));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<List<JobItemPlan>> PlanAsync(JobRunContext context, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(context);
            return Task.FromResult(new List<JobItemPlan> { new JobItemPlan("digest:" + context.NowUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture)) });
        }

        /// <inheritdoc />
        public async Task<JobItemOutcome> ExecuteAsync(JobItemContext context, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(context);
            long flagged = await _Review.SendDigestAsync(token).ConfigureAwait(false);
            return JobItemOutcome.Ok("{\"flagged\":" + flagged.ToString(CultureInfo.InvariantCulture) + ",\"sent\":" + (flagged > 0 ? "true" : "false") + "}");
        }

        #endregion
    }
}
