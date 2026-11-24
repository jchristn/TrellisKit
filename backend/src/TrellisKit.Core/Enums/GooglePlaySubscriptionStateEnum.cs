namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Google Play subscriptionsv2 state.
    /// </summary>
    public enum GooglePlaySubscriptionStateEnum
    {
        /// <summary>
        /// Unknown state.
        /// </summary>
        Unspecified,

        /// <summary>
        /// Purchase pending.
        /// </summary>
        Pending,

        /// <summary>
        /// Active.
        /// </summary>
        Active,

        /// <summary>
        /// Paused.
        /// </summary>
        Paused,

        /// <summary>
        /// In grace period.
        /// </summary>
        InGracePeriod,

        /// <summary>
        /// On hold (account hold).
        /// </summary>
        OnHold,

        /// <summary>
        /// Canceled; access continues until expiry.
        /// </summary>
        Canceled,

        /// <summary>
        /// Expired.
        /// </summary>
        Expired,

        /// <summary>
        /// Pending purchase canceled.
        /// </summary>
        PendingPurchaseCanceled
    }
}
