namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Result of an admin bulk metadata change.
    /// </summary>
    public class MetadataBulkResponse
    {
        #region Public-Members

        /// <summary>
        /// Objects changed.
        /// </summary>
        public int Updated { get; set; } = 0;

        /// <summary>
        /// Requested identifiers that do not exist (at most 500).
        /// </summary>
        public List<string> NotFound { get; set; } = new List<string>();

        #endregion
    }
}
