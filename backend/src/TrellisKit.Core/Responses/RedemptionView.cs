namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A sign-up code redemption as shown to admins.
    /// </summary>
    public class RedemptionView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Code identifier.
        /// </summary>
        public string SignupCodeId { get; set; } = String.Empty;

        /// <summary>
        /// Code text.
        /// </summary>
        public string? Code { get; set; } = null;

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
        /// Redeemed.
        /// </summary>
        public DateTime RedeemedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Free membership end, or null for lifetime.
        /// </summary>
        public DateTime? MembershipEndsUtc { get; set; } = null;

        /// <summary>
        /// Redeemed during sign-up.
        /// </summary>
        public bool IsNewAccount { get; set; } = false;

        /// <summary>
        /// Status.
        /// </summary>
        public RedemptionStatusEnum Status { get; set; } = RedemptionStatusEnum.Active;

        /// <summary>
        /// Started a paid subscription after redeeming.
        /// </summary>
        public bool ConvertedToPaid { get; set; } = false;

        /// <summary>
        /// When revoked.
        /// </summary>
        public DateTime? RevokedUtc { get; set; } = null;

        /// <summary>
        /// Why revoked.
        /// </summary>
        public string? RevokeReason { get; set; } = null;

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
