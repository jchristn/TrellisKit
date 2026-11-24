namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A bounded sample of a segment: at most 10 members and the total.
    /// </summary>
    public class BroadcastSample
    {
        #region Public-Members

        /// <summary>
        /// Members (at most 10).
        /// </summary>
        public List<UserSummary> Objects { get; set; } = new List<UserSummary>();

        /// <summary>
        /// Every recipient.
        /// </summary>
        public long TotalRecords { get; set; } = 0;

        #endregion
    }
}
