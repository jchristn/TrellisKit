namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A moderator row filtered to the assignments covering one forum (docs/PHASE3_CONTRACT.md sections 3.2 and 11.6).
    /// </summary>
    public class ForumModeratorView : ModeratorListItem
    {
        #region Public-Members

        /// <summary>
        /// Forum (an assignment for this forum) or AllForums.
        /// </summary>
        public string Scope { get; set; } = "Forum";

        /// <summary>
        /// The forum of a Forum assignment, or null for AllForums.
        /// </summary>
        public string? ForumId { get; set; } = null;

        #endregion
    }
}
