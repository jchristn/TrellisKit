namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// PUT /api/v1/admin/forums/order body.
    /// </summary>
    public class ForumOrderRequest
    {
        #region Public-Members

        /// <summary>
        /// Every non-archived forum identifier exactly once.
        /// </summary>
        public List<string>? ForumIds { get; set; } = null;

        #endregion
    }
}
