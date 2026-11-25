namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of PUT /api/v1/admin/lessons/{id}/catalog-entries: the complete replacement set of a lesson's links.
    /// </summary>
    public class LessonCatalogEntriesRequest
    {
        #region Public-Members

        /// <summary>
        /// Links, at most 50, each catalog entry at most once. An empty list removes every link.
        /// </summary>
        public List<LessonCatalogEntryItemRequest>? Items { get; set; } = null;

        #endregion
    }
}
