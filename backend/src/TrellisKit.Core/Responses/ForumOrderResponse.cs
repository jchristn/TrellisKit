namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// PUT /api/v1/admin/forums/order result.
    /// </summary>
    public class ForumOrderResponse
    {
        #region Public-Members

        /// <summary>
        /// Forums reordered.
        /// </summary>
        public int Updated { get; set; } = 0;

        #endregion
    }
}
