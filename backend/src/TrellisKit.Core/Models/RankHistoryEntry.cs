namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// One rank award in a member's rank history.
    /// </summary>
    public class RankHistoryEntry
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix lin_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Member identifier.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Zero-based display order. Minimum 0.
        /// </summary>
        public int Position
        {
            get { return _Position; }
            set { _Position = Math.Max(0, value); }
        }

        /// <summary>
        /// Rank awarded.
        /// </summary>
        public string Rank { get; set; } = String.Empty;

        /// <summary>
        /// Who awarded the rank, up to 100 characters, or null.
        /// </summary>
        public string? AwardedByName { get; set; } = null;

        /// <summary>
        /// Member identifier of who awarded the rank, or null.
        /// </summary>
        public string? AwardedByUserId { get; set; } = null;

        /// <summary>
        /// Affiliation, up to 100 characters, or null.
        /// </summary>
        public string? Affiliation { get; set; } = null;

        /// <summary>
        /// Date awarded as YYYY-MM-DD, or null.
        /// </summary>
        public string? DateAwarded { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private int _Position = 0;

        #endregion
    }
}
