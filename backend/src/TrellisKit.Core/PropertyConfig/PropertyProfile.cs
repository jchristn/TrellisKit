namespace TrellisKit.Core.PropertyConfig
{
    using System;

    /// <summary>
    /// Profile taxonomy: which profile sections exist and their allowed values.
    /// </summary>
    public class PropertyProfile
    {
        #region Public-Members

        /// <summary>
        /// Affiliation field.
        /// </summary>
        public PropertyAffiliationField Affiliation { get; set; } = new PropertyAffiliationField();

        /// <summary>
        /// Rank field.
        /// </summary>
        public PropertyRankField Rank { get; set; } = new PropertyRankField();

        /// <summary>
        /// Rank history section.
        /// </summary>
        public PropertyRankHistoryField RankHistory { get; set; } = new PropertyRankHistoryField();

        /// <summary>
        /// Goals field.
        /// </summary>
        public PropertyGoalsField Goals { get; set; } = new PropertyGoalsField();

        #endregion
    }
}
