namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One entitlement in the membership history.
    /// </summary>
    public class MembershipHistoryItem : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Entitlement identifier (ent_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Source.
        /// </summary>
        public MembershipSourceEnum Source { get; set; } = MembershipSourceEnum.Subscription;

        /// <summary>
        /// Store for subscriptions, or null.
        /// </summary>
        public StoreEnum? Store { get; set; } = null;

        /// <summary>
        /// Start (UTC).
        /// </summary>
        public DateTime StartsUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// End (UTC), or null for lifetime.
        /// </summary>
        public DateTime? EndsUtc { get; set; } = null;

        /// <summary>
        /// Status.
        /// </summary>
        public EntitlementStatusEnum Status { get; set; } = EntitlementStatusEnum.Active;

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
