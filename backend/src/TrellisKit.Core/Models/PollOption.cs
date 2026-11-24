namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// A poll option row.
    /// </summary>
    public class PollOption
    {
        #region Public-Members

        /// <summary>
        /// Identifier (pop_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Poll identifier.
        /// </summary>
        public string PollId { get; set; } = String.Empty;

        /// <summary>
        /// Option label (1 to 140 characters).
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Lowercase normalized label (unique per poll).
        /// </summary>
        public string LabelNormalized { get; set; } = String.Empty;

        /// <summary>
        /// Display position (1 based).
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Votes for this option.
        /// </summary>
        public int VoteCount { get; set; } = 0;

        /// <summary>
        /// Creation time (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
