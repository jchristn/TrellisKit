namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One source of membership. A member is active when any entitlement is active; the latest end wins and a null end means lifetime.
    /// </summary>
    public class Entitlement
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix ent_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Member identifier.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Source.
        /// </summary>
        public MembershipSourceEnum Source { get; set; } = MembershipSourceEnum.Subscription;

        /// <summary>
        /// Source identifier: subscription id, sign-up code redemption id, or the entitlement id for comps.
        /// </summary>
        public string SourceId { get; set; } = String.Empty;

        /// <summary>
        /// Start (UTC).
        /// </summary>
        public DateTime StartsUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// End (UTC), or null for lifetime.
        /// </summary>
        public DateTime? EndsUtc { get; set; } = null;

        /// <summary>
        /// When revoked (UTC), or null.
        /// </summary>
        public DateTime? RevokedUtc { get; set; } = null;

        /// <summary>
        /// Why it was revoked, or null.
        /// </summary>
        public string? RevokeReason { get; set; } = null;

        /// <summary>
        /// Why it was granted (comps), or null.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// How an admin comp was granted (DurationMonths, FixedEndDate, Lifetime), or null for other sources.
        /// </summary>
        public GrantTypeEnum? GrantType { get; set; } = null;

        /// <summary>
        /// Administrator who granted it, or null.
        /// </summary>
        public string? GrantedByUserId { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last updated (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether the entitlement grants membership at a point in time.
        /// </summary>
        /// <param name="nowUtc">Time (UTC).</param>
        /// <returns>True when started, not ended, and not revoked.</returns>
        public bool IsActiveAt(DateTime nowUtc)
        {
            return RevokedUtc == null && StartsUtc <= nowUtc && (EndsUtc == null || EndsUtc.Value > nowUtc);
        }

        /// <summary>
        /// Status at a point in time.
        /// </summary>
        /// <param name="nowUtc">Time (UTC).</param>
        /// <returns>Status.</returns>
        public EntitlementStatusEnum StatusAt(DateTime nowUtc)
        {
            if (RevokedUtc != null) return EntitlementStatusEnum.Revoked;
            if (StartsUtc > nowUtc) return EntitlementStatusEnum.Scheduled;
            if (EndsUtc != null && EndsUtc.Value <= nowUtc) return EntitlementStatusEnum.Expired;
            return EntitlementStatusEnum.Active;
        }

        #endregion
    }
}
