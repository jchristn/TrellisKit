namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Outcomes of sign-up code redemptions.
    /// </summary>
    public class RedemptionOutcomes
    {
        #region Public-Members

        /// <summary>
        /// Redemptions whose free membership is still running.
        /// </summary>
        public long Active { get; set; } = 0;

        /// <summary>
        /// Redeemers who later started a paid subscription.
        /// </summary>
        public long ConvertedToPaid { get; set; } = 0;

        /// <summary>
        /// Redemptions that ended without the member having any active membership now.
        /// </summary>
        public long Lapsed { get; set; } = 0;

        /// <summary>
        /// Redemptions revoked by an administrator.
        /// </summary>
        public long Revoked { get; set; } = 0;

        #endregion
    }
}
