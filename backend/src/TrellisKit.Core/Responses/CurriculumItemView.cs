namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A curriculum item with the viewer's (or the inspected member's) state.
    /// </summary>
    public class CurriculumItemView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (cui_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Curriculum.
        /// </summary>
        public string CurriculumId { get; set; } = String.Empty;

        /// <summary>
        /// Rank group.
        /// </summary>
        public string? OptionKey { get; set; } = null;

        /// <summary>
        /// Rank group label.
        /// </summary>
        public string? OptionLabel { get; set; } = null;

        /// <summary>
        /// Lesson, Series, CatalogEntry, or Custom.
        /// </summary>
        public CurriculumItemTypeEnum ItemType { get; set; } = CurriculumItemTypeEnum.Lesson;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string? TargetId { get; set; } = null;

        /// <summary>
        /// Lesson or series title; null for CatalogEntry (clients resolve catalog titles) and Custom.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// False when a Lesson or Series target no longer exists or is unpublished.
        /// </summary>
        public bool TargetAvailable { get; set; } = true;

        /// <summary>
        /// Custom text.
        /// </summary>
        public string? CustomText { get; set; } = null;

        /// <summary>
        /// Position.
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Whether required.
        /// </summary>
        public bool Required { get; set; } = true;

        /// <summary>
        /// Whether members can mark it complete (CatalogEntry and Custom).
        /// </summary>
        public bool SelfCompletable { get; set; } = false;

        /// <summary>
        /// Incomplete, Completed (from library progress), SelfComplete, or Verified.
        /// </summary>
        public string State { get; set; } = "Incomplete";

        /// <summary>
        /// Whether the item counts as done (required CatalogEntry and Custom items need Verified).
        /// </summary>
        public bool Satisfied { get; set; } = false;

        /// <summary>
        /// Verifier.
        /// </summary>
        public string? VerifiedBy { get; set; } = null;

        /// <summary>
        /// Verification time.
        /// </summary>
        public DateTime? VerifiedUtc { get; set; } = null;

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
