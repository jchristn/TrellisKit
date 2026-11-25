namespace TrellisKit.Server.Services.CatalogImport
{
    using System.Collections.Generic;

    /// <summary>Closed version 1 interchange for explicitly licensed text-only catalog drafts. No remote media, lessons or graph writes.</summary>
    public sealed class CatalogImportManifest
    {
        /// <summary>Supported version, exactly 1.</summary>
        public int Version { get; set; }
        /// <summary>At most the source limit, never more than 100 keyed records.</summary>
        public List<CatalogImportRecord> Entries { get; set; } = new List<CatalogImportRecord>();
    }
}
