namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// PUT /api/v1/admin/ideas/statuses/order: every active status exactly once, in the new order.
    /// </summary>
    public class IdeaStatusOrderRequest
    {
        #region Public-Members

        /// <summary>
        /// Status identifiers in display order.
        /// </summary>
        public List<string>? IdeaStatusIds { get; set; } = null;

        #endregion
    }
}
