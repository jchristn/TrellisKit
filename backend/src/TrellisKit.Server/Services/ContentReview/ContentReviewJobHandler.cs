namespace TrellisKit.Server.Services.ContentReview
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Server.Services.Jobs;

    /// <summary>
    /// content.review: every 5 minutes, reviews the Pending rows of the content changelog through the Moderation task
    /// role, one item per row (docs/INTEGRATION.md content review contract). Plans nothing while AI is off or the role has
    /// no executors, so rows wait in Pending. Enabled by default. Thread safety: stateless.
    /// </summary>
    public sealed class ContentReviewJobHandler : IJobHandler
    {
        #region Public-Members

        /// <inheritdoc />
        public string Key
        {
            get { return ContentReviewService.ReviewJobKey; }
        }

        /// <inheritdoc />
        public JobHandlerDefaults Defaults { get; } = new JobHandlerDefaults
        {
            Name = "Review member content",
            Description = "Reviews new and edited member contributions in the content changelog for Terms of Service violations through the Moderation role, and warns administrators about anything flagged.",
            Enabled = true,
            IntervalType = JobIntervalTypeEnum.EveryNMinutes,
            IntervalValue = 5,
            MaxConcurrency = 4,
            MaxAttempts = 3,
            BudgetUsdPerRun = 25,
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
        public ContentReviewJobHandler(ContentReviewService review)
        {
            _Review = review ?? throw new ArgumentNullException(nameof(review));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<List<JobItemPlan>> PlanAsync(JobRunContext context, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(context);
            return _Review.PlanReviewAsync(token);
        }

        /// <inheritdoc />
        public Task<JobItemOutcome> ExecuteAsync(JobItemContext context, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(context);
            return _Review.ReviewAsync(context.Item.TargetKey, token);
        }

        #endregion
    }
}
