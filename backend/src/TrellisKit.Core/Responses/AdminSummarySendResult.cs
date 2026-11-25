namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Result of POST /api/v1/admin/summary/send.
    /// </summary>
    public class AdminSummarySendResult
    {
        #region Public-Members

        /// <summary>
        /// Summarized local date (yyyy-MM-dd).
        /// </summary>
        public string Date { get; set; } = String.Empty;

        /// <summary>
        /// Addresses a message was queued for.
        /// </summary>
        public List<string> Queued { get; set; } = new List<string>();

        /// <summary>
        /// Addresses skipped because the summary of this date was already sent to them.
        /// </summary>
        public List<string> AlreadySent { get; set; } = new List<string>();

        #endregion
    }
}
