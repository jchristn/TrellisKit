namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Feedback focus in a thread request.
    /// </summary>
    public class FeedbackRequest
    {
        #region Public-Members

        /// <summary>
        /// What the member wants reviewed (0 to 500 characters).
        /// </summary>
        public string? Focus { get; set; } = null;

        #endregion
    }
}
