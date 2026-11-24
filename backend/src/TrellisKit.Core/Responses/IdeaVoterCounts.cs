namespace TrellisKit.Core.Responses
{
    /// <summary>
    /// Staff planning view of an idea's voters by membership state (docs/PHASE5_CONTRACT.md section 7.1): counts only,
    /// never names.
    /// </summary>
    public class IdeaVoterCounts
    {
        #region Public-Members

        /// <summary>
        /// Every vote.
        /// </summary>
        public long Total { get; set; } = 0;

        /// <summary>
        /// Votes by accounts with an active membership.
        /// </summary>
        public long Members { get; set; } = 0;

        /// <summary>
        /// Votes by accounts without an active membership.
        /// </summary>
        public long NonMembers { get; set; } = 0;

        #endregion
    }
}
