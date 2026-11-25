namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>Structured training catalog CatalogActionRequest contract. Values are validated by the catalog service.</summary>
    public class CatalogActionRequest
    {
        /// <summary>Required current revision.</summary>
        public long? ExpectedRevision { get; set; } = null;

        /// <summary>Review or archive note, at most 2000 characters.</summary>
        public string? Note { get; set; } = null;

    }
}
