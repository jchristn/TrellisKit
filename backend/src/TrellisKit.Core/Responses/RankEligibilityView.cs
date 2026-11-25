namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One member in the eligibility list: current rank, next rank, and requirement progress.
    /// </summary>
    public class RankEligibilityView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Member summary.
        /// </summary>
        public UserSummary? User { get; set; } = null;

        /// <summary>
        /// Current effective rank.
        /// </summary>
        public EffectiveRankView Current { get; set; } = new EffectiveRankView();

        /// <summary>
        /// Next rank and requirement progress.
        /// </summary>
        public RankNextView? Next { get; set; } = null;

        /// <summary>
        /// Whether every configured requirement of the next rank is met.
        /// </summary>
        public bool Eligible { get; set; } = false;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
