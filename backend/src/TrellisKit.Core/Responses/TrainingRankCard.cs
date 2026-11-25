namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// The "Your rank" card (ranks.enabled): the member's effective rank in the first Rank profile field (profile order),
    /// the next step with its requirements, and the curriculum for that step, from the same rules as GET /api/v1/me/rank.
    /// The overview carries null instead while ranks are off or the property has no Rank field.
    /// </summary>
    public class TrainingRankCard
    {
        #region Public-Members

        /// <summary>
        /// The effective rank (verified, or self-reported from the profile; source None when the member holds no rank).
        /// </summary>
        public EffectiveRankView Rank { get; set; } = new EffectiveRankView();

        /// <summary>
        /// The next rank step with its requirements and progress, or null at the top of the field.
        /// </summary>
        public RankNextView? Next { get; set; } = null;

        /// <summary>
        /// The curriculum of the next step (the next requirement's curriculum, otherwise the first published curriculum
        /// visible to the member for the field), with the member's progress, or null.
        /// </summary>
        public CurriculumView? Curriculum { get; set; } = null;

        /// <summary>
        /// Whether clients show "verified" or "self-reported" labels (ranks.showVerificationLabels).
        /// </summary>
        public bool ShowVerificationLabels { get; set; } = true;

        #endregion
    }
}
