namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/membership.
    /// </summary>
    public class MembershipResponse
    {
        #region Public-Members

        /// <summary>
        /// Effective membership.
        /// </summary>
        public MembershipSummary Membership { get; set; } = new MembershipSummary();

        /// <summary>
        /// Stripe offer.
        /// </summary>
        public WebOffer WebOffer { get; set; } = new WebOffer();

        /// <summary>
        /// In-app product identifier.
        /// </summary>
        public string StoreProductId { get; set; } = String.Empty;

        /// <summary>
        /// UUID to pass as appAccountToken (iOS) and obfuscatedAccountId (Android).
        /// </summary>
        public string StoreAccountToken { get; set; } = String.Empty;

        /// <summary>
        /// Membership history, latest start first (at most 5), the total, and the enumeration path.
        /// </summary>
        public BoundedPreview<MembershipHistoryItem> History
        {
            get { return _History; }
            set { _History = value ?? new BoundedPreview<MembershipHistoryItem>(); }
        }

        #endregion

        #region Private-Members

        private BoundedPreview<MembershipHistoryItem> _History = new BoundedPreview<MembershipHistoryItem>();

        #endregion
    }
}
