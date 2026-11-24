namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// A member's entitlements and subscriptions with the computed membership.
    /// </summary>
    public class MembershipState
    {
        #region Public-Members

        /// <summary>
        /// Entitlements, newest first. Never null.
        /// </summary>
        public List<Entitlement> Entitlements
        {
            get { return _Entitlements; }
            set { _Entitlements = value ?? new List<Entitlement>(); }
        }

        /// <summary>
        /// Subscriptions, newest first. Never null.
        /// </summary>
        public List<Subscription> Subscriptions
        {
            get { return _Subscriptions; }
            set { _Subscriptions = value ?? new List<Subscription>(); }
        }

        /// <summary>
        /// Computed membership.
        /// </summary>
        public MembershipSummary Summary { get; set; } = new MembershipSummary();

        #endregion

        #region Private-Members

        private List<Entitlement> _Entitlements = new List<Entitlement>();
        private List<Subscription> _Subscriptions = new List<Subscription>();

        #endregion
    }
}
