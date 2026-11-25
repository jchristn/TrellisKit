namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// A curriculum (table curricula, id prefix cur_): a list of lessons, series, catalog entries, and custom items, optionally grouped by the options of a Rank profile field.
    /// </summary>
    public class Curriculum : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (cur_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Name (1 to 120 characters).
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Description (at most 2000 characters).
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Rank profile field whose options group the items, or null.
        /// </summary>
        public string? FieldKey { get; set; } = null;

        /// <summary>
        /// Who sees it once published.
        /// </summary>
        public CurriculumVisibilityEnum Visibility { get; set; } = CurriculumVisibilityEnum.Members;

        /// <summary>
        /// Draft, Published, or Archived.
        /// </summary>
        public CurriculumStatusEnum Status { get; set; } = CurriculumStatusEnum.Draft;

        /// <summary>
        /// Sort position (0 to 100000).
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Number of items (read only).
        /// </summary>
        public int ItemCount { get; set; } = 0;

        /// <summary>
        /// Creator.
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// First publication time.
        /// </summary>
        public DateTime? PublishedUtc { get; set; } = null;

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
