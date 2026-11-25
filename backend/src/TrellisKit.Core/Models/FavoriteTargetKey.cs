namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A target a member may have saved (state lookups).
    /// </summary>
    public class FavoriteTargetKey
    {
        #region Public-Members

        /// <summary>
        /// Target type.
        /// </summary>
        public FavoriteTargetTypeEnum TargetType { get; set; } = FavoriteTargetTypeEnum.Lesson;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        #endregion
    }
}
