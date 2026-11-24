namespace TrellisKit.Core.PropertyConfig
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The rank profile field and its allowed values.
    /// </summary>
    public class PropertyRankField
    {
        #region Public-Members

        /// <summary>
        /// Whether members can set a rank.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Display label of the rank.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Display label of the level within a rank.
        /// </summary>
        public string LevelLabel { get; set; } = String.Empty;

        /// <summary>
        /// Highest level within a rank. Minimum 0 (no levels), maximum 100.
        /// </summary>
        public int MaxLevel { get; set; } = 0;

        /// <summary>
        /// Allowed ranks in display order. Keys are unique.
        /// </summary>
        public List<PropertyRankValue> Values { get; set; } = new List<PropertyRankValue>();

        #endregion
    }
}
