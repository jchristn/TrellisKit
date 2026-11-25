namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One link of PUT /api/v1/admin/lessons/{id}/catalog-entries.
    /// </summary>
    public class LessonCatalogEntryItemRequest
    {
        #region Public-Members

        /// <summary>
        /// Published catalog entry identifier (cex_).
        /// </summary>
        public string? CatalogEntryId { get; set; } = null;

        /// <summary>
        /// Where in the lesson video the technique appears (0 to 86400 seconds), or null.
        /// </summary>
        public int? StartSeconds { get; set; } = null;

        /// <summary>
        /// Order (1 to 50), or null to use the item's place in the list. Ties keep list order; stored positions are 1 to n.
        /// </summary>
        public int? Position { get; set; } = null;

        /// <summary>
        /// Editor (default) or Suggested (a suggestion an editor approved).
        /// </summary>
        public LessonCatalogLinkSourceEnum? Source { get; set; } = null;

        /// <summary>
        /// Labels, or null.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags, or null.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
