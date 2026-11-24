namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Filters of the content review queue (POST /api/v1/admin/content-review/enumerate). Empty lists and null values
    /// do not filter. Not thread safe.
    /// </summary>
    public class ContentChangeFilter
    {
        #region Public-Members

        /// <summary>
        /// Review states to include.
        /// </summary>
        public List<ContentReviewStatusEnum> Statuses { get; set; } = new List<ContentReviewStatusEnum>();

        /// <summary>
        /// Rows carrying any of these categories.
        /// </summary>
        public List<ContentReviewCategoryEnum> Categories { get; set; } = new List<ContentReviewCategoryEnum>();

        /// <summary>
        /// Severities to include.
        /// </summary>
        public List<ContentReviewSeverityEnum> Severities { get; set; } = new List<ContentReviewSeverityEnum>();

        /// <summary>
        /// Lowest severity to include, or null.
        /// </summary>
        public ContentReviewSeverityEnum? MinimumSeverity { get; set; } = null;

        /// <summary>
        /// Target types to include (exact names).
        /// </summary>
        public List<string> TargetTypes { get; set; } = new List<string>();

        /// <summary>
        /// Target identifier, or null.
        /// </summary>
        public string? TargetId { get; set; } = null;

        /// <summary>
        /// Clients to include.
        /// </summary>
        public List<ContentChangeClientEnum> Clients { get; set; } = new List<ContentChangeClientEnum>();

        /// <summary>
        /// Actions to include.
        /// </summary>
        public List<ContentChangeActionEnum> Actions { get; set; } = new List<ContentChangeActionEnum>();

        /// <summary>
        /// Rows whose actor is this member, or null.
        /// </summary>
        public string? ActorUserId { get; set; } = null;

        /// <summary>
        /// Rows whose author is this member, or null.
        /// </summary>
        public string? AuthorUserId { get; set; } = null;

        #endregion
    }
}
