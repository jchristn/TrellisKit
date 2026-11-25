namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Models;

    /// <summary>
    /// PUT /api/v1/admin/ranks/requirements response.
    /// </summary>
    public class RankRequirementsResponse
    {
        #region Public-Members

        /// <summary>
        /// Rank field key.
        /// </summary>
        public string FieldKey { get; set; } = String.Empty;

        /// <summary>
        /// Requirements stored.
        /// </summary>
        public int Updated { get; set; } = 0;

        /// <summary>
        /// The stored requirements (at most 200).
        /// </summary>
        public List<RankRequirement> Requirements { get; set; } = new List<RankRequirement>();

        #endregion
    }
}
