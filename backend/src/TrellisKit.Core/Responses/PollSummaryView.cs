namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A poll in a thread list item.
    /// </summary>
    public class PollSummaryView
    {
        #region Public-Members

        /// <summary>
        /// Poll identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Whether the poll is closed.
        /// </summary>
        public bool IsClosed { get; set; } = false;

        /// <summary>
        /// Scheduled close, or null.
        /// </summary>
        public DateTime? ClosesUtc { get; set; } = null;

        /// <summary>
        /// Distinct voters.
        /// </summary>
        public int TotalVoters { get; set; } = 0;

        /// <summary>
        /// Whether the caller voted.
        /// </summary>
        public bool HasVoted { get; set; } = false;

        #endregion
    }
}
