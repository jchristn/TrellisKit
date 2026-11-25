namespace TrellisKit.Server.Services.CatalogImport
{
    using System;
    using TrellisKit.Core.Requests;

    /// <summary>One stable source identity and structured draft.</summary>
    public sealed class CatalogImportRecord
    {
        /// <summary>Stable external identifier, 1..100 letters, digits, dots, underscores or hyphens; case sensitive.</summary>
        public string Key { get; set; } = String.Empty;
        /// <summary>Structured text. Identity, workflow, sources and metadata are assigned by the import service.</summary>
        public CatalogEntryRequest Entry { get; set; } = new CatalogEntryRequest();
    }
}
