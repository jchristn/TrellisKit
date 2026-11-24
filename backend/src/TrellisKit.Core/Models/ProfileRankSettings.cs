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

        #endregion
    }
}
