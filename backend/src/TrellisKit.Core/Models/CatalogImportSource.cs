namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Property-scoped catalog import provenance.</summary>
    public sealed class CatalogImportSource
    {
        /// <summary>Identifier cis_.</summary>
        public string Id { get; set; } = String.Empty;
        /// <summary>Source name, 2..200 characters.</summary>
        public string Name { get; set; } = String.Empty;
        /// <summary>Reviewed HTTPS manifest URL, at most 2000 characters, no credentials or fragments.</summary>
        public string Url { get; set; } = String.Empty;
        /// <summary>CC0-1.0, CC-BY-4.0, PublicDomain or Unlicense.</summary>
        public string License { get; set; } = String.Empty;
        /// <summary>Attribution, at most 2000 characters; required for CC-BY-4.0.</summary>
        public string? Attribution { get; set; } = null;
        /// <summary>Participates in imports after explicit license review. Default false.</summary>
        public bool Enabled { get; set; }
        /// <summary>Manifest item limit, 1..100, default 20. Larger manifests are rejected without partial truncation.</summary>
        public int MaxEntries { get; set; } = 20;
        /// <summary>Optimistic concurrency revision.</summary>
        public long Revision { get; set; } = 1;
        /// <summary>Explicit source review timestamp, or null after every edit.</summary>
        public DateTime? ReviewedUtc { get; set; }
        /// <summary>Reviewer identity, null for the break-glass administrator.</summary>
        public string? ReviewedByUserId { get; set; }
        /// <summary>Last real fetch attempt; oldest sources are selected first by the bounded scheduler.</summary>
        public DateTime? LastAttemptUtc { get; set; }
        /// <summary>Creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        /// <summary>Change timestamp.</summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
        /// <summary>Bounded searchable metadata labels.</summary>
        public List<string> Labels { get; set; } = new List<string>();
        /// <summary>Bounded searchable metadata tags.</summary>
        public Dictionary<string,string> Tags { get; set; } = new Dictionary<string,string>();
    }
}
