namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// POST /api/v1/admin/ranks/awards: award a verified rank.
    /// </summary>
    public class RankAwardRequest
    {
        #region Public-Members

        /// <summary>
        /// Member to promote.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Rank profile field key (default the first enabled Rank field).
        /// </summary>
        public string? FieldKey { get; set; } = null;

        /// <summary>
        /// Option key (case-insensitive).
        /// </summary>
        public string? OptionKey { get; set; } = null;

        /// <summary>
        /// Level within the option (default 0; 0 to the option's maxLevel).
        /// </summary>
        public int? Level { get; set; } = null;

        /// <summary>
        /// Note (at most 500 characters).
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Optional event the award happened at.
        /// </summary>
        public string? EventId { get; set; } = null;

        /// <summary>
        /// Labels of the award.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags of the award.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
