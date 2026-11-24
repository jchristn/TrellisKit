namespace TrellisKit.Core.Requests
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Filters of the admin profile field enumeration. Null means not filtered.
    /// </summary>
    public class ProfileFieldFilter
    {
        #region Public-Members

        /// <summary>
        /// Type.
        /// </summary>
        public ProfileFieldTypeEnum? Type { get; set; } = null;

        /// <summary>
        /// When collected.
        /// </summary>
        public ProfileCollectAtEnum? CollectAt { get; set; } = null;

        /// <summary>
        /// Visibility.
        /// </summary>
        public ProfileVisibilityEnum? Visibility { get; set; } = null;

        /// <summary>
        /// Enabled.
        /// </summary>
        public bool? Enabled { get; set; } = null;

        /// <summary>
        /// Built in.
        /// </summary>
        public bool? BuiltIn { get; set; } = null;

        /// <summary>
        /// Filterable.
        /// </summary>
        public bool? Filterable { get; set; } = null;

        /// <summary>
        /// Shown on cards.
        /// </summary>
        public bool? ShowOnCard { get; set; } = null;

        #endregion
    }
}
