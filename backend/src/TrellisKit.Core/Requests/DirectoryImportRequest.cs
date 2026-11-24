namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Import venues (docs/PHASE4_CONTRACT.md section 3.9).
    /// </summary>
    public class DirectoryImportRequest
    {
        #region Public-Members

        /// <summary>
        /// Rows (at most 1000); a row whose slug matches a venue updates it.
        /// </summary>
        public List<VenueRequest>? Rows { get; set; } = null;

        /// <summary>
        /// Validate only (the default).
        /// </summary>
        public bool DryRun { get; set; } = true;

        #endregion
    }
}
