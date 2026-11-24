namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// A rank history entry as returned to clients.
    /// </summary>
    public class RankHistoryView
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Rank key from the property profile.rank.values.
        /// </summary>
        public string Rank { get; set; } = String.Empty;

        /// <summary>
        /// Name of who awarded the rank.
        /// </summary>
        public string? AwardedByName { get; set; } = null;

        /// <summary>
        /// Member identifier of who awarded the rank, or null.
        /// </summary>
        public string? AwardedByUserId { get; set; } = null;

        /// <summary>
        /// Affiliation.
        /// </summary>
        public string? Affiliation { get; set; } = null;

        /// <summary>
        /// YYYY-MM-DD.
        /// </summary>
        public string? DateAwarded { get; set; } = null;

        #endregion
    }
}
