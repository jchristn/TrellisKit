namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/admin/metrics/membership.
    /// </summary>
    public class MembershipMetrics
    {
        #region Public-Members

        /// <summary>
        /// Members (not deleted).
        /// </summary>
        public long TotalMembers { get; set; } = 0;

        /// <summary>
        /// Members with an active membership.
        /// </summary>
        public long ActiveMembers { get; set; } = 0;

        /// <summary>
        /// Active through a paid subscription.
        /// </summary>
        public long PaidActive { get; set; } = 0;

        /// <summary>
        /// Active only through codes or comps.
        /// </summary>
        public long FreeActive { get; set; } = 0;

        /// <summary>
        /// Paid active members by store (Apple, Google, Stripe). Never null.
        /// </summary>
        public Dictionary<string, long> ByStore
        {
            get { return _ByStore; }
            set { _ByStore = value ?? new Dictionary<string, long>(); }
        }

        /// <summary>
        /// Active members by source (Subscription, SignupCode, AdminComp). Never null.
        /// </summary>
        public Dictionary<string, long> BySource
        {
            get { return _BySource; }
            set { _BySource = value ?? new Dictionary<string, long>(); }
        }

        /// <summary>
        /// Accounts created in the last 30 days.
        /// </summary>
        public long NewMembers30d { get; set; } = 0;

        /// <summary>
        /// Paid active members times the annual price.
        /// </summary>
        public decimal EstimatedArrUsd { get; set; } = 0m;

        /// <summary>
        /// Members whose membership ended in the last 30 days and who are not active now.
        /// </summary>
        public long Lapsed30d { get; set; } = 0;

        #endregion

        #region Private-Members

        private Dictionary<string, long> _ByStore = new Dictionary<string, long>();
        private Dictionary<string, long> _BySource = new Dictionary<string, long>();

        #endregion
    }
}
