namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// PUT /api/v1/me/lineage: replace the caller's lineage for one Rank field.
    /// </summary>
    public class LineageRequest
    {
        #region Public-Members

        /// <summary>
        /// Rank profile field key (default the first enabled Rank field).
        /// </summary>
        public string? FieldKey { get; set; } = null;

        /// <summary>
        /// Entries in display order (at most 50).
        /// </summary>
        public List<LineageEntryInput>? Entries { get; set; } = null;

        #endregion
    }
}
