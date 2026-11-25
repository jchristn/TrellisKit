namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Structured training catalog CatalogRelationship contract. Values are validated by the catalog service.</summary>
    public class CatalogRelationship
    {
        /// <summary>Relationship identifier, cer_.</summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>Source catalog entry.</summary>
        public string SourceId { get; set; } = String.Empty;

        /// <summary>Target catalog entry.</summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>Regression, Progression, Substitution, Transition, Prerequisite, or Counterpose.</summary>
        public string Kind { get; set; } = "Transition";

        /// <summary>Relationship note, at most 1000 characters.</summary>
        public string? Note { get; set; } = null;

        /// <summary>Metadata labels.</summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>Metadata tags.</summary>
        public Dictionary<string,string> Tags { get; set; } = new Dictionary<string,string>();

        /// <summary>Created UTC time.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Resolved target name, read only.</summary>
        public string? TargetName { get; set; } = null;

        /// <summary>Resolved target slug, read only.</summary>
        public string? TargetSlug { get; set; } = null;

        /// <summary>Resolved target entry kind, read only.</summary>
        public string? TargetKind { get; set; } = null;

    }
}
