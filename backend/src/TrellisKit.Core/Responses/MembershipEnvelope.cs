namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// A body carrying only the membership.
    /// </summary>
    public class MembershipEnvelope
    {
        #region Public-Members

        /// <summary>
        /// Effective membership.
        /// </summary>
        public MembershipSummary Membership { get; set; } = new MembershipSummary();

        #endregion
    }
}
