namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// One of a member's idea votes (account export section ideaVotes).
    /// </summary>
    public class IdeaVoteRecord
    {
        #region Public-Members

        /// <summary>
        /// Vote identifier (idv_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Idea identifier.
        /// </summary>
        public string IdeaId { get; set; } = String.Empty;

        /// <summary>
        /// Idea title at export time.
        /// </summary>
        public string IdeaTitle { get; set; } = String.Empty;

        /// <summary>
        /// When the vote was cast (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
