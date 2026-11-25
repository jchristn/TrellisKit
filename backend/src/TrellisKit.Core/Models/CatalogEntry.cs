namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Structured training catalog CatalogEntry contract. Values are validated by the catalog service.</summary>
    public class CatalogEntry : CatalogEntryData
    {
        /// <summary>Identifier, cex_.</summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>Draft, InReview, Published, Rejected, or Archived.</summary>
        public string Status { get; set; } = "Draft";

        /// <summary>Shared content moderation state, independent of editorial workflow.</summary>
        public TrellisKit.Core.Enums.ModerationStateEnum ModerationState { get; set; } = TrellisKit.Core.Enums.ModerationStateEnum.None;

        /// <summary>Monotonic optimistic concurrency revision.</summary>
        public long Revision { get; set; } = 1;

        /// <summary>Author identifier.</summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>Reviewer identifier.</summary>
        public string? ReviewedByUserId { get; set; } = null;

        /// <summary>Private review note.</summary>
        public string? ReviewNote { get; set; } = null;

        /// <summary>Last review UTC time.</summary>
        public DateTime? ReviewedUtc { get; set; } = null;

        /// <summary>Last approved publication UTC time.</summary>
        public DateTime? PublishedUtc { get; set; } = null;

        /// <summary>Soft deletion time.</summary>
        public DateTime? DeletedUtc { get; set; } = null;

        /// <summary>Creation UTC time.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Last change UTC time.</summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    }
}
