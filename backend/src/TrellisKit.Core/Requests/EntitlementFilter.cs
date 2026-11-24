namespace TrellisKit.Core.Requests
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Typed filters of a member's entitlements (comps and membership history).
    /// </summary>
    public class EntitlementFilter
    {
        #region Public-Members

        /// <summary>
        /// Member whose entitlements are listed (required).
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Only this source, or null.
        /// </summary>
        public MembershipSourceEnum? Source { get; set; } = null;

        /// <summary>
        /// Only this status at the current time, or null.
        /// </summary>
        public EntitlementStatusEnum? Status { get; set; } = null;

        #endregion
    }
}
