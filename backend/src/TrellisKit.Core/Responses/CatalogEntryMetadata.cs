namespace TrellisKit.Core.Responses
{
    using System.Collections.Generic;

    /// <summary>Explicit empty metadata on catalog entries whose metadata is not persisted.</summary>
    public class CatalogEntryMetadata : IMetadataPayload
    {
        /// <summary>Labels; empty for immutable configuration entries.</summary>
        public List<string> Labels { get; set; } = new List<string>();
        /// <summary>Tags; empty for immutable configuration entries.</summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
    }
}
