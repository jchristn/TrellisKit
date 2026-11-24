namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// One rank history entry in a profile update. Entries without an id are new.
    /// </summary>
    public class RankHistoryEntryRequest
    {
        #region Public-Members

        /// <summary>
        /// Existing entry identifier, or null for a new entry.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Rank key (required).
        /// </summary>
        public string? Rank { get; set; } = null;

        /// <summary>
        /// Who awarded the rank, up to 100 characters.
        /// </summary>
        public string? AwardedByName { get; set; } = null;

        /// <summary>
        /// Member identifier of who awarded the rank, or null.
        /// </summary>
        public string? AwardedByUserId { get; set; } = null;

        /// <summary>
        /// Affiliation, up to 100 characters.
        /// </summary>
        public string? Affiliation { get; set; } = null;

        /// <summary>
        /// YYYY-MM-DD, not in the future.
        /// </summary>
        public string? DateAwarded { get; set; } = null;

        #endregion
    }
}
