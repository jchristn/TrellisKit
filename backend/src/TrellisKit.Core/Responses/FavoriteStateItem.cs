namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Whether the caller saved one target.
    /// </summary>
    public class FavoriteStateItem
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

        /// <summary>
        /// Whether the caller saved it.
        /// </summary>
        public bool Saved { get; set; } = false;

        /// <summary>
        /// The save, or null.
        /// </summary>
        public string? FavoriteId { get; set; } = null;

        #endregion
    }
}
