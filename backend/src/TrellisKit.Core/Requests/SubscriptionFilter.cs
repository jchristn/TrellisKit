namespace TrellisKit.Core.Requests
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Admin subscription list filter.
    /// </summary>
    public class SubscriptionFilter
    {
        #region Public-Members

        /// <summary>
        /// Store, or null.
        /// </summary>
        public StoreEnum? Store { get; set; } = null;

        /// <summary>
        /// Status, or null.
        /// </summary>
        public SubscriptionStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// Member, or null.
        /// </summary>
        public string? UserId { get; set; } = null;

        #endregion
    }
}
