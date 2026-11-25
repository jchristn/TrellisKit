namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// PUT /api/v1/me/lineage response: the caller's lineage of one Rank field.
    /// </summary>
    public class LineageResponse
    {
        #region Public-Members

        /// <summary>
        /// Rank field key.
        /// </summary>
        public string FieldKey { get; set; } = String.Empty;

        /// <summary>
        /// Entries in display order (at most 50).
        /// </summary>
        public List<LineageEntryView> Entries { get; set; } = new List<LineageEntryView>();

        #endregion
    }
}
