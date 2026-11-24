namespace TrellisKit.Core.PropertyConfig
{
    using System;

    /// <summary>
    /// The rank history profile section.
    /// </summary>
    public class PropertyRankHistoryField
    {
        #region Public-Members

        /// <summary>
        /// Whether members can record a rank history.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Display label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Display label of the person who awarded a rank.
        /// </summary>
        public string AwardedByLabel { get; set; } = String.Empty;

        /// <summary>
        /// Maximum entries per member. Default 12. Minimum 1, maximum 100.
        /// </summary>
        public int MaxEntries { get; set; } = 12;

        #endregion
    }
}
