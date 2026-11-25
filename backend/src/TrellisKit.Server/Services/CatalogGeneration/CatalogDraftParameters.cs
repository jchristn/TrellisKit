namespace TrellisKit.Server.Services.CatalogGeneration
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Models;

    /// <summary>Administrator-supplied, licensed material for one bounded draft-generation run.</summary>
    public sealed class CatalogDraftParameters
    {
        /// <summary>One through twenty movement names present in the source; each becomes an independent job item.</summary>
        public List<string> Topics { get; set; } = new List<string>();
        /// <summary>Exercise or Technique.</summary>
        public string Kind { get; set; } = "Exercise";
        /// <summary>Configured directory program key, or null.</summary>
        public string? Program { get; set; } = null;
        /// <summary>Licensed source text, 40 through 10000 characters. Never fetched automatically.</summary>
        public string SourceText { get; set; } = String.Empty;
        /// <summary>Source metadata; its license must be explicitly reviewed by an administrator.</summary>
        public CatalogSource Source { get; set; } = new CatalogSource();
        /// <summary>Explicit administrator acknowledgement that the supplied source may be reused under its stated license.</summary>
        public bool SourceReviewed { get; set; }
        /// <summary>Maximum output tokens per movement, 256 through 12000, default 4000.</summary>
        public int MaxOutputTokens { get; set; } = 4000;
    }
}
