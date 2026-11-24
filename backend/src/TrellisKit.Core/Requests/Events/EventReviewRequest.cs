namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Approve or reject a member proposal (resolved decision 10).
    /// </summary>
    public class EventReviewRequest
    {
        #region Public-Members

        /// <summary>
        /// Note to the proposer (optional on approve, 3 to 1000 on reject).
        /// </summary>
        public string? ReviewNote { get; set; } = null;

        /// <summary>
        /// Approve: publish now (default true); false leaves a Draft.
        /// </summary>
        public bool? Publish { get; set; } = null;

        /// <summary>
        /// Approve and publish: NewEvent fan-out (default false).
        /// </summary>
        public bool? NotifyMembers { get; set; } = null;

        #endregion
    }
}
