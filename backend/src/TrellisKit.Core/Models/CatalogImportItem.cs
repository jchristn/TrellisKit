namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Property-scoped catalog import provenance.</summary>
    public sealed class CatalogImportItem
    {
        /// <summary>Source identifier.</summary>
        public string SourceId { get; set; } = String.Empty;
        /// <summary>Stable external record key, 1..100 characters.</summary>
        public string ItemKey { get; set; } = String.Empty;
        /// <summary>Associated draft identifier, including deleted drafts that must not be recreated.</summary>
        public string EntryId { get; set; } = String.Empty;
        /// <summary>Canonical imported content SHA256.</summary>
        public string SourceHash { get; set; } = String.Empty;
        /// <summary>Catalog revision last written by import; any later edit protects the draft.</summary>
        public long EntryRevision { get; set; }
        /// <summary>Source configuration revision that was reviewed and imported.</summary>
        public long SourceRevision { get; set; }
    }
}
