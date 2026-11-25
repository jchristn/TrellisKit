namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>Structured training catalog CatalogEntryRequest contract. Values are validated by the catalog service.</summary>
    public class CatalogEntryRequest : TrellisKit.Core.Models.CatalogEntryData
    {
        /// <summary>Required on edits, omitted on create.</summary>
        public long? ExpectedRevision { get; set; } = null;

    }
}
