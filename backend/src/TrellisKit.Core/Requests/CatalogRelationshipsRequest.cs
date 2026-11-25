namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>Structured training catalog CatalogRelationshipsRequest contract. Values are validated by the catalog service.</summary>
    public class CatalogRelationshipsRequest
    {
        /// <summary>Required current source entry revision.</summary>
        public long? ExpectedRevision { get; set; } = null;

        /// <summary>Replacement outgoing relationships, at most one hundred.</summary>
        public List<TrellisKit.Core.Models.CatalogRelationship> Relationships { get; set; } = new List<TrellisKit.Core.Models.CatalogRelationship>();

    }
}
