namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The terms of a trending score (docs/PHASE3_CONTRACT.md section 5.1).
    /// </summary>
    public class TrendingBreakdown
    {
        #region Public-Members

        /// <summary>
        /// Reactions.
        /// </summary>
        public long Reactions { get; set; } = 0;

        /// <summary>
        /// Visible comments.
        /// </summary>
        public long Comments { get; set; } = 0;

        /// <summary>
        /// Distinct commenters other than the author.
        /// </summary>
        public long UniqueCommenters { get; set; } = 0;

        /// <summary>
        /// Hours since publish.
        /// </summary>
        public double AgeHours { get; set; } = 0;

        /// <summary>
        /// Weighted sum.
        /// </summary>
        public double Numerator { get; set; } = 0;

        /// <summary>
        /// Age term.
        /// </summary>
        public double Denominator { get; set; } = 0;

        /// <summary>
        /// Live score from these counts and the current weights (Numerator / Denominator, 0 or more). The stored
        /// thread.trendingScore, which orders the list, is the value as of computedUtc and catches up on the next
        /// worker pass.
        /// </summary>
        public double Score { get; set; } = 0;

        #endregion
    }
}
