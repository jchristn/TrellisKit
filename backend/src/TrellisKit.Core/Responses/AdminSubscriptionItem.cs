namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A subscription in admin lists.
    /// </summary>
    public class AdminSubscriptionItem : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Member name.
        /// </summary>
        public string? MemberDisplayName { get; set; } = null;

        /// <summary>
        /// Member email.
        /// </summary>
        public string? MemberEmail { get; set; } = null;

        /// <summary>
        /// Store.
        /// </summary>
        public StoreEnum Store { get; set; } = StoreEnum.Stripe;

        /// <summary>
        /// Product.
        /// </summary>
        public string ProductId { get; set; } = String.Empty;

        /// <summary>
        /// Status.
        /// </summary>
        public SubscriptionStatusEnum Status { get; set; } = SubscriptionStatusEnum.Active;

        /// <summary>
        /// Period start.
        /// </summary>
        public DateTime? CurrentPeriodStartUtc { get; set; } = null;

        /// <summary>
        /// Period end.
        /// </summary>
        public DateTime? CurrentPeriodEndUtc { get; set; } = null;

        /// <summary>
        /// Renews.
        /// </summary>
        public bool AutoRenew { get; set; } = false;

        /// <summary>
        /// Environment.
        /// </summary>
        public StoreEnvironmentEnum Environment { get; set; } = StoreEnvironmentEnum.Production;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last event.
        /// </summary>
        public DateTime? LastEventUtc { get; set; } = null;

        /// <summary>
        /// Labels visible to the caller (staff: entries only for staff). Never null.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags visible to the caller (staff: keys only for staff). Never null.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
