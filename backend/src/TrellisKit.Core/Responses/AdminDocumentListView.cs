namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/admin/documents: every document the property declares (bounded by property.json, at most 30).
    /// </summary>
    public class AdminDocumentListView
    {
        #region Public-Members

        /// <summary>
        /// Documents in property.json order.
        /// </summary>
        public List<AdminDocumentView> Objects { get; set; } = new List<AdminDocumentView>();

        /// <summary>
        /// Whether the property folder wins on change (SingleProperty).
        /// </summary>
        public bool SyncsFromFolder { get; set; } = false;

        #endregion
    }
}
