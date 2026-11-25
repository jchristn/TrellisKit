namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>Administrator catalog source configuration request.</summary>
    public sealed class CatalogImportSourceRequest
    {
        /// <summary>Expected revision on update; omitted on creation.</summary>
        public long? ExpectedRevision { get; set; }
        /// <summary>Source name, 2..200 characters.</summary>
        public string Name { get; set; } = String.Empty;
        /// <summary>Absolute HTTPS manifest URL.</summary>
        public string Url { get; set; } = String.Empty;
        /// <summary>Reviewed permissive license identifier.</summary>
        public string License { get; set; } = String.Empty;
        /// <summary>Attribution, at most 2000 characters.</summary>
        public string? Attribution { get; set; }
        /// <summary>Enabled after explicit source review; default false.</summary>
        public bool Enabled { get; set; }
        /// <summary>Maximum manifest entries, 1..100, default 20.</summary>
        public int MaxEntries { get; set; } = 20;
        /// <summary>Labels.</summary>
        public List<string> Labels { get; set; } = new List<string>();
        /// <summary>Tags.</summary>
        public Dictionary<string,string> Tags { get; set; } = new Dictionary<string,string>();
    }
}
