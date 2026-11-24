namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A registered property (deployment-level registry row, docs/MULTIPROPERTY_CONTRACT.md section 7.1).
    /// </summary>
    public class PropertyRecord
    {
        #region Public-Members

        /// <summary>
        /// Opaque property id (app_{k-sortable}_{random}). Never renamed.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Cosmetic slug (folder names, image tags, infrastructure names). Never identifies the property.
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Display name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Lifecycle status.
        /// </summary>
        public PropertyStatusEnum Status { get; set; } = PropertyStatusEnum.Disabled;

        /// <summary>
        /// The validated property.json document (read whole, never queried inside).
        /// </summary>
        public string DocumentJson { get; set; } = String.Empty;

        /// <summary>
        /// Folder the property was seeded from (brand assets are served from it), or null.
        /// </summary>
        public string? SourceDirectory { get; set; } = null;

        /// <summary>
        /// Content hash of property.json when it was last synchronized from SourceDirectory, or null.
        /// </summary>
        public string? DirectoryHash { get; set; } = null;

        /// <summary>
        /// Last enabled time.
        /// </summary>
        public DateTime? EnabledUtc { get; set; } = null;

        /// <summary>
        /// Last disabled time.
        /// </summary>
        public DateTime? DisabledUtc { get; set; } = null;

        /// <summary>
        /// Reason given when disabled.
        /// </summary>
        public string? DisabledReason { get; set; } = null;

        /// <summary>
        /// When the purge may start (status Deleting).
        /// </summary>
        public DateTime? DeleteScheduledUtc { get; set; } = null;

        /// <summary>
        /// When the purge started.
        /// </summary>
        public DateTime? PurgeStartedUtc { get; set; } = null;

        /// <summary>
        /// When the purge finished (status Deleted).
        /// </summary>
        public DateTime? PurgedUtc { get; set; } = null;

        /// <summary>
        /// Created time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last change time (feeds the registry reload check).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the property's profile fields were first seeded, or null.
        /// </summary>
        public DateTime? ProfileFieldsSeededUtc { get; set; } = null;

        /// <summary>
        /// Hash of the property document's profile field section last merged into the definitions, or null.
        /// </summary>
        public string? ProfileFieldsHash { get; set; } = null;

        #endregion
    }
}
