namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/admin/members/{id}: the Me fields plus admin-only state.
    /// </summary>
    public class AdminMemberDetail : MeResponse
    {
        #region Public-Members

        /// <summary>
        /// Banned.
        /// </summary>
        public bool IsBanned { get; set; } = false;

        /// <summary>
        /// Ban reason.
        /// </summary>
        public string? BannedReason { get; set; } = null;

        /// <summary>
        /// Deleted and anonymized.
        /// </summary>
        public bool IsDeleted { get; set; } = false;

        /// <summary>
        /// Last seen.
        /// </summary>
        public DateTime? LastSeenUtc { get; set; } = null;

        /// <summary>
        /// Newest subscriptions (at most 5), the total, and the enumeration path.
        /// </summary>
        public BoundedPreview<AdminSubscriptionItem> Subscriptions
        {
            get { return _Subscriptions; }
            set { _Subscriptions = value ?? new BoundedPreview<AdminSubscriptionItem>(); }
        }

        /// <summary>
        /// Newest sign-up code redemptions (at most 5), the total, and the enumeration path.
        /// </summary>
        public BoundedPreview<RedemptionView> Redemptions
        {
            get { return _Redemptions; }
            set { _Redemptions = value ?? new BoundedPreview<RedemptionView>(); }
        }

        /// <summary>
        /// Newest comps (at most 5), the total, and the enumeration path.
        /// </summary>
        public BoundedPreview<CompView> Comps
        {
            get { return _Comps; }
            set { _Comps = value ?? new BoundedPreview<CompView>(); }
        }

        /// <summary>
        /// Most recently created active sessions (at most 5), the total, and the enumeration path.
        /// </summary>
        public BoundedPreview<SessionView> Sessions
        {
            get { return _Sessions; }
            set { _Sessions = value ?? new BoundedPreview<SessionView>(); }
        }

        /// <summary>
        /// Membership history (every entitlement, latest start first; at most 5), the total, and the enumeration path.
        /// </summary>
        public BoundedPreview<MembershipHistoryItem> MembershipHistory
        {
            get { return _MembershipHistory; }
            set { _MembershipHistory = value ?? new BoundedPreview<MembershipHistoryItem>(); }
        }

        #endregion

        #region Private-Members

        private BoundedPreview<AdminSubscriptionItem> _Subscriptions = new BoundedPreview<AdminSubscriptionItem>();
        private BoundedPreview<RedemptionView> _Redemptions = new BoundedPreview<RedemptionView>();
        private BoundedPreview<CompView> _Comps = new BoundedPreview<CompView>();
        private BoundedPreview<SessionView> _Sessions = new BoundedPreview<SessionView>();
        private BoundedPreview<MembershipHistoryItem> _MembershipHistory = new BoundedPreview<MembershipHistoryItem>();

        #endregion
    }
}
