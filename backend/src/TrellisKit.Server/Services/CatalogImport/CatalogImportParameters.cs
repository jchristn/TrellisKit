namespace TrellisKit.Server.Services.CatalogImport
{
    using System.Collections.Generic;

    /// <summary>Optional bounded source selection for scheduled or manual import.</summary>
    public sealed class CatalogImportParameters
    {
        /// <summary>Up to 20 source IDs. Empty selects the 20 least recently attempted reviewed enabled sources.</summary>
        public List<string> SourceIds { get; set; } = new List<string>();
    }
}
