namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>Administrator catalog source configuration request.</summary>
    public sealed class CatalogImportReviewRequest
    {
        /// <summary>Expected configuration revision.</summary>
        public long ExpectedRevision { get; set; }
        /// <summary>Explicit confirmation that all records in the manifest may be reused under the configured license.</summary>
        public bool LicenseReviewed { get; set; }
    }
}
