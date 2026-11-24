namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/admin/subscriptions/{id}.
    /// </summary>
    public class AdminSubscriptionDetail : AdminSubscriptionItem
    {
        #region Public-Members

        /// <summary>
        /// Store identifier.
        /// </summary>
        public string StoreSubscriptionId { get; set; } = String.Empty;

        /// <summary>
        /// Newest events (at most 5), the total, and the enumeration path.
        /// </summary>
        public BoundedPreview<SubscriptionEventView> Events
        {
            get { return _Events; }
            set { _Events = value ?? new BoundedPreview<SubscriptionEventView>(); }
        }

        #endregion

        #region Private-Members

        private BoundedPreview<SubscriptionEventView> _Events = new BoundedPreview<SubscriptionEventView>();

        #endregion
    }
}
