namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Structured training catalog CatalogSource contract. Values are validated by the catalog service.</summary>
    public class CatalogSource
    {
        /// <summary>Manual, Model, or Import provenance method.</summary>
        public string Method { get; set; } = "Manual";
        /// <summary>Generating model identifier, or null.</summary>
        public string? Model { get; set; } = null;
        /// <summary>When the source was read, UTC, or null.</summary>
        public DateTime? RetrievedUtc { get; set; } = null;

        /// <summary>Public HTTPS source URL; optional only for original contributions.</summary>
        public string? Url { get; set; } = null;

        /// <summary>Source title, at most 200 characters.</summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>Permissive license identifier, reviewed before publication.</summary>
        public string License { get; set; } = String.Empty;

        /// <summary>Attribution, at most 2000 characters.</summary>
        public string? Attribution { get; set; } = null;

    }
}
