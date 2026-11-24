namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A conversation member (docs/PHASE5_CONTRACT.md section 2.1).
    /// </summary>
    public class ConversationMemberView
    {
        #region Public-Members

        /// <summary>
        /// Member.
        /// </summary>
        public UserSummary User { get; set; } = new UserSummary();

        /// <summary>
        /// Role.
        /// </summary>
        public ConversationMemberRoleEnum Role { get; set; } = ConversationMemberRoleEnum.Member;

        /// <summary>
        /// Joined (UTC).
        /// </summary>
        public DateTime JoinedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
