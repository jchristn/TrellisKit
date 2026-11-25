namespace TrellisKit.Server.Services.CatalogGeneration
{
    using System;

    /// <summary>Bounded item evidence linking directly to the reviewable draft.</summary>
    public sealed class CatalogDraftResult
    {
        /// <summary>Requested movement name.</summary>
        public string Topic { get; set; } = String.Empty;
        /// <summary>Planned, CreatedDraft, Existing, or Unsupported.</summary>
        public string Outcome { get; set; } = String.Empty;
        /// <summary>Catalog entry identifier when one exists.</summary>
        public string? EntryId { get; set; }
        /// <summary>Stable entry slug.</summary>
        public string Slug { get; set; } = String.Empty;
        /// <summary>Source/input fingerprint for idempotency and audit.</summary>
        public string SourceHash { get; set; } = String.Empty;
        /// <summary>True when the job performed no model call or catalog write.</summary>
        public bool DryRun { get; set; }
        /// <summary>Generated entries always require human review and explicit publication.</summary>
        public bool HumanReviewRequired { get; set; } = true;
    }
}
