namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Effective membership computed from every entitlement.
    /// </summary>
    public class MembershipSummary
    {
        #region Public-Members

        /// <summary>
        /// Whether any entitlement is active.
        /// </summary>
        public bool IsActive { get; set; } = false;

        /// <summary>
        /// Primary source, or null.
        /// </summary>
        public MembershipSourceEnum? Source { get; set; } = null;

        /// <summary>
        /// Store when the source is Subscription, or null.
        /// </summary>
        public StoreEnum? Store { get; set; } = null;

        /// <summary>
        /// When membership coverage ends; null means lifetime or no membership.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>
        /// Whether the primary subscription renews.
        /// </summary>
        public bool AutoRenew { get; set; } = false;

        /// <summary>
        /// Whether a renewing paid subscription takes over when the free membership ends.
        /// </summary>
        public bool WillConvertToPaid { get; set; } = false;

        #endregion
    }
}
