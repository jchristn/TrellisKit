namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/members/{id}/rank: a member's public effective rank per visible Rank field and their lineage.
    /// </summary>
    public class MemberRankResponse
    {
        #region Public-Members

        /// <summary>
        /// Member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Whether clients show verified or self-reported labels (ranks.showVerificationLabels).
        /// </summary>
        public bool ShowVerificationLabels { get; set; } = true;

        /// <summary>
        /// One entry per enabled Rank field the caller may see (at most 10).
        /// </summary>
        public List<EffectiveRankView> Ranks { get; set; } = new List<EffectiveRankView>();

        /// <summary>
        /// Lineage entries of the visible fields (at most 100).
        /// </summary>
        public List<LineageEntryView> Lineage { get; set; } = new List<LineageEntryView>();

        #endregion
    }
}
