namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// One self-reported entry of a member's rank lineage (table lineageentries, id prefix lin_): who promoted them to an option and when, optionally verified by the named promoter or a SystemAdmin.
    /// </summary>
    public class LineageEntry : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (lin_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Key of the Rank profile field.
        /// </summary>
        public string FieldKey { get; set; } = String.Empty;

        /// <summary>
        /// Option key.
        /// </summary>
        public string OptionKey { get; set; } = String.Empty;

        /// <summary>
        /// Promoter when they are a member of the property, or null.
        /// </summary>
        public string? PromotedByUserId { get; set; } = null;

        /// <summary>
        /// Promoter name when they are not a member (at most 120 characters), or null.
        /// </summary>
        public string? PromotedByName { get; set; } = null;

        /// <summary>
        /// When the promotion happened, or null when unknown.
        /// </summary>
        public DateTime? PromotedUtc { get; set; } = null;

        /// <summary>
        /// Whether the named promoter or a SystemAdmin verified the entry.
        /// </summary>
        public bool Verified { get; set; } = false;

        /// <summary>
        /// Verifier member identifier.
        /// </summary>
        public string? VerifiedBy { get; set; } = null;

        /// <summary>
        /// Verification time.
        /// </summary>
        public DateTime? VerifiedUtc { get; set; } = null;

        /// <summary>
        /// Display position.
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update time.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
