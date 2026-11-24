namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The ideas block of GET /api/v1/property: the ideas board settings clients need (docs/PHASE5_CONTRACT.md section 14).
    /// </summary>
    public class PublicIdeasView
    {
        #region Public-Members

        /// <summary>
        /// Who may submit ideas: Direct (members) or StaffOnly. Default StaffOnly.
        /// </summary>
        public IdeaSubmissionModeEnum MemberSubmissions { get; set; } = IdeaSubmissionModeEnum.StaffOnly;

        /// <summary>
        /// Whether the anonymous public board (/api/v1/public/ideas) serves Public ideas. Default false.
        /// </summary>
        public bool PublicBoardEnabled { get; set; } = false;

        /// <summary>
        /// Whether members may vote on ideas with a closed status. Default false.
        /// </summary>
        public bool VoteOnClosed { get; set; } = false;

        #endregion
    }
}
