namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// Field ids of the seeded fields that map the Phase 1 to 2 profile columns (docs/MULTIPROPERTY_CONTRACT.md section
    /// 9.13), for the value backfill. A null id skips that column.
    /// </summary>
    public class ProfileLegacyFieldIds
    {
        #region Public-Members

        /// <summary>
        /// Rank field (users.rank, users.ranklevel).
        /// </summary>
        public string? Rank { get; set; } = null;

        /// <summary>
        /// Affiliation field (users.affiliation).
        /// </summary>
        public string? Affiliation { get; set; } = null;

        /// <summary>
        /// City field (users.city).
        /// </summary>
        public string? City { get; set; } = null;

        /// <summary>
        /// Region field (users.region).
        /// </summary>
        public string? Region { get; set; } = null;

        /// <summary>
        /// Country field (users.country).
        /// </summary>
        public string? Country { get; set; } = null;

        /// <summary>
        /// Goals field (usergoals, users.goalsother).
        /// </summary>
        public string? Goals { get; set; } = null;

        /// <summary>
        /// Key of the goals option that carries free text, or null.
        /// </summary>
        public string? GoalsFreeTextKey { get; set; } = null;

        /// <summary>
        /// Rank history field (rankhistoryentries).
        /// </summary>
        public string? RankHistory { get; set; } = null;

        #endregion
    }
}
