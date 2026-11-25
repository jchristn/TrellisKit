namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>Structured training catalog CatalogEntryView contract. Values are validated by the catalog service.</summary>
    public class CatalogEntryView : TrellisKit.Core.Models.CatalogEntry
    {
        /// <summary>Caller capabilities.</summary>
        public CatalogEntryViewer Viewer { get; set; } = new CatalogEntryViewer();

    }
}
