namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// PUT /api/v1/forums/{id}/follow body.
    /// </summary>
    public class ForumFollowRequest
    {
        #region Public-Members

        /// <summary>
        /// Follow level (NewThreads).
        /// </summary>
        public string? Level { get; set; } = null;

        #endregion
    }
}
