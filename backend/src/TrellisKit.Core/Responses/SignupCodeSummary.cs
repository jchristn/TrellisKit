namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// GET /api/v1/admin/signup-codes/summary.
    /// </summary>
    public class SignupCodeSummary
    {
        #region Public-Members

        /// <summary>
        /// Codes with status Active.
        /// </summary>
        public long ActiveCodes { get; set; } = 0;

        /// <summary>
        /// All redemptions.
        /// </summary>
        public long TotalRedemptions { get; set; } = 0;

        /// <summary>
        /// Redemptions in the last 30 days.
        /// </summary>
        public long Redemptions30d { get; set; } = 0;

        /// <summary>
        /// Members with an active code membership.
        /// </summary>
        public long ActiveFreeMembers { get; set; } = 0;

        /// <summary>
        /// Redeemers who later paid.
        /// </summary>
        public long ConvertedToPaid { get; set; } = 0;

        /// <summary>
        /// ConvertedToPaid divided by total redemptions, 0 to 1.
        /// </summary>
        public double ConversionRate { get; set; } = 0;

        #endregion
    }
}
