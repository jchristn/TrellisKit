namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// Rank settings of a Rank profile field.
    /// </summary>
    public class ProfileRankSettings
    {
        #region Public-Members

        /// <summary>
        /// Level label, 0 to 30 characters; empty means no level.
        /// </summary>
        public string LevelLabel { get; set; } = String.Empty;

        /// <summary>
        /// Maximum level, 0 to 20.
        /// </summary>
        public int MaxLevel { get; set; } = 0;

        /// <summary>
        /// Verified rank awarding rules (rank.awarding), or null for the neutral defaults. Option maxLevel values may not
        /// exceed MaxLevel; awarding keys must name options of the field.
        /// </summary>
        public ProfileRankAwarding? Awarding { get; set; } = null;

        #endregion
    }
}
