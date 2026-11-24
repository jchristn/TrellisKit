namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// The trending score inputs of one thread (docs/PHASE3_CONTRACT.md section 5.1).
    /// </summary>
    public class TrendingInput
    {
        #region Public-Members

        /// <summary>
        /// Thread identifier.
        /// </summary>
        public string ThreadId { get; set; } = String.Empty;

        /// <summary>
        /// Publish time.
        /// </summary>
        public DateTime PublishedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Reactions on the conversation target.
        /// </summary>
        public int Reactions { get; set; } = 0;

        /// <summary>
        /// Visible comments.
        /// </summary>
        public int Comments { get; set; } = 0;

        /// <summary>
        /// Distinct visible commenters excluding the author.
        /// </summary>
        public int UniqueCommenters { get; set; } = 0;

        #endregion
    }
}
