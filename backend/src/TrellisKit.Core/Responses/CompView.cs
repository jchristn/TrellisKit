namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An admin comp (free membership).
    /// </summary>
    public class CompView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Entitlement identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// How the comp was granted.
        /// </summary>
        public GrantTypeEnum GrantType { get; set; } = GrantTypeEnum.Lifetime;

        /// <summary>
        /// Grant in words.
        /// </summary>
        public string GrantSummary { get; set; } = String.Empty;

        /// <summary>
        /// Start.
        /// </summary>
        public DateTime StartsUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// End, or null for lifetime.
        /// </summary>
        public DateTime? EndsUtc { get; set; } = null;

        /// <summary>
        /// Why it was granted.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Granting admin.
        /// </summary>
        public string? GrantedByUserId { get; set; } = null;

        /// <summary>
        /// When ended early.
        /// </summary>
        public DateTime? RevokedUtc { get; set; } = null;

        /// <summary>
        /// Why it was ended.
        /// </summary>
        public string? RevokeReason { get; set; } = null;

        /// <summary>
        /// Status.
        /// </summary>
        public EntitlementStatusEnum Status { get; set; } = EntitlementStatusEnum.Active;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

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
