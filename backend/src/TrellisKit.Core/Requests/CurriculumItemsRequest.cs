namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// PUT /api/v1/admin/curricula/{id}/items: replace the items in order.
    /// </summary>
    public class CurriculumItemsRequest
    {
        #region Public-Members

        /// <summary>
        /// Items in order (at most 500).
        /// </summary>
        public List<CurriculumItemInput>? Items { get; set; } = null;

        #endregion
    }
}
