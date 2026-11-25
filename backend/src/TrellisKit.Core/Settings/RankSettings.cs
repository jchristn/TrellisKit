namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Verified ranks, curricula, and attendance switches (docs/TRELLISKIT.md, Ranks, curricula, and attendance).
    /// </summary>
    public class RankSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether verified ranks, curricula, and attendance are on for the property. Key ranks.enabled. Default false;
        /// while false every rank, curriculum, and attendance route answers 404 NotAvailable.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Whether profiles and member cards show the verified or self-reported label next to Rank fields. Key
        /// ranks.showVerificationLabels. Default true (turn off while verifying members in bulk).
        /// </summary>
        public bool ShowVerificationLabels { get; set; } = true;

        #endregion
    }
}
