namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Filters of a member's saves enumeration. Labels, tags, and the created window come from the EnumerationQuery.
    /// </summary>
    public class FavoriteFilter
    {
        #region Public-Members

        /// <summary>
        /// Owner (required).
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Target types to include; empty includes every type.
        /// </summary>
        public List<FavoriteTargetTypeEnum> TargetTypes
        {
            get { return _TargetTypes; }
            set { _TargetTypes = value ?? new List<FavoriteTargetTypeEnum>(); }
        }

        /// <summary>
        /// Only saves in this collection of the owner, or null.
        /// </summary>
        public string? CollectionId { get; set; } = null;

        #endregion

        #region Private-Members

        private List<FavoriteTargetTypeEnum> _TargetTypes = new List<FavoriteTargetTypeEnum>();

        #endregion
    }
}
