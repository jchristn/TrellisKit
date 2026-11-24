namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A requester membership snapshot (staff only).
    /// </summary>
    public class TicketMembershipView
    {
        #region Public-Members

        /// <summary>
        /// Whether the requester had a membership.
        /// </summary>
        public bool IsActive { get; set; } = false;

        /// <summary>
        /// Membership source, or null.
        /// </summary>
        public string? Source { get; set; } = null;

        /// <summary>
        /// Store, or null.
        /// </summary>
        public string? Store { get; set; } = null;

        /// <summary>
        /// Coverage end, or null.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        #endregion
    }
}
