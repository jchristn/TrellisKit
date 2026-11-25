namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One item in PUT /api/v1/admin/curricula/{id}/items.
    /// </summary>
    public class CurriculumItemInput
    {
        #region Public-Members

        /// <summary>
        /// Existing item identifier to keep (progress stays), or null for a new item.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Rank group, or null.
        /// </summary>
        public string? OptionKey { get; set; } = null;

        /// <summary>
        /// Lesson, Series, CatalogEntry, or Custom.
        /// </summary>
        public string? ItemType { get; set; } = null;

        /// <summary>
        /// Lesson, series, or catalog entry identifier.
        /// </summary>
        public string? TargetId { get; set; } = null;

        /// <summary>
        /// Custom text (Custom only).
        /// </summary>
        public string? CustomText { get; set; } = null;

        /// <summary>
        /// Whether required (default true).
        /// </summary>
        public bool? Required { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
