namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A poll on a public sample thread.
    /// </summary>
    public class PublicPollSummaryView
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
        /// Scheduled close.
        /// </summary>
        public DateTime? ClosesUtc { get; set; } = null;

        /// <summary>
        /// Distinct voters.
        /// </summary>
        public int TotalVoters { get; set; } = 0;

        #endregion
    }
}
