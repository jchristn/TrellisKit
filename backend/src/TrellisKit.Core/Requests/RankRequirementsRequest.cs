namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// PUT /api/v1/admin/ranks/requirements: replace every requirement of one Rank field.
    /// </summary>
    public class RankRequirementsRequest
    {
        #region Public-Members

        /// <summary>
        /// Rank profile field key (default the first enabled Rank field).
        /// </summary>
        public string? FieldKey { get; set; } = null;

        /// <summary>
        /// Requirements (at most 200).
        /// </summary>
        public List<RankRequirementInput>? Requirements { get; set; } = null;

        #endregion
    }
}
