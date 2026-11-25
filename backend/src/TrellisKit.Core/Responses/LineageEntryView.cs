namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A lineage entry with the promoter's summary.
    /// </summary>
    public class LineageEntryView : IMetadataPayload
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
        /// Rank field key.
        /// </summary>
        public string FieldKey { get; set; } = String.Empty;

        /// <summary>
        /// Option key.
        /// </summary>
        public string OptionKey { get; set; } = String.Empty;

        /// <summary>
        /// Option label.
        /// </summary>
        public string? OptionLabel { get; set; } = null;

        /// <summary>
        /// Promoter member, or null.
        /// </summary>
        public string? PromotedByUserId { get; set; } = null;

        /// <summary>
        /// Promoter summary, or null.
        /// </summary>
        public UserSummary? PromotedBy { get; set; } = null;

        /// <summary>
        /// Promoter name when not a member.
        /// </summary>
        public string? PromotedByName { get; set; } = null;

        /// <summary>
        /// When.
        /// </summary>
        public DateTime? PromotedUtc { get; set; } = null;

        /// <summary>
        /// Verified by the promoter or a SystemAdmin.
        /// </summary>
        public bool Verified { get; set; } = false;

        /// <summary>
        /// Verifier.
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
