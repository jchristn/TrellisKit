namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>Structured training catalog CatalogEntryViewer contract. Values are validated by the catalog service.</summary>
    public class CatalogEntryViewer
    {
        /// <summary>May edit this entry.</summary>
        public bool CanEdit { get; set; } = false;

        /// <summary>May submit this entry for review.</summary>
        public bool CanSubmit { get; set; } = false;

        /// <summary>May approve or reject this entry.</summary>
        public bool CanReview { get; set; } = false;

    }
}
