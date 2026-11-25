namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// One item of a curriculum (table curriculumitems, id prefix cui_). CatalogEntry targets are stored as text identifiers without a foreign key.
    /// </summary>
    public class CurriculumItem : IMetadataPayload
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
        /// Rank group (an option of the curriculum's Rank field), or null.
        /// </summary>
        public string? OptionKey { get; set; } = null;

        /// <summary>
        /// Lesson, Series, CatalogEntry, or Custom.
        /// </summary>
        public CurriculumItemTypeEnum ItemType { get; set; } = CurriculumItemTypeEnum.Lesson;

        /// <summary>
        /// Lesson, series, or catalog entry identifier (null for Custom).
        /// </summary>
        public string? TargetId { get; set; } = null;

        /// <summary>
        /// Custom item text (Custom only, 1 to 300 characters).
        /// </summary>
        public string? CustomText { get; set; } = null;

        /// <summary>
        /// Position within the curriculum.
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Whether the item counts toward rank requirements. Required CatalogEntry and Custom items need verification; optional ones accept self-completion.
        /// </summary>
        public bool Required { get; set; } = true;

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
