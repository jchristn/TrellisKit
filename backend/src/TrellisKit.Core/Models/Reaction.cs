namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One member's reaction key on a target (contract section 6).
    /// </summary>
    public class Reaction
    {
        #region Public-Members

        /// <summary>
        /// Identifier (rxn_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Target type.
        /// </summary>
        public TargetTypeEnum TargetType { get; set; } = TargetTypeEnum.FeedPost;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Member who reacted.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Reaction key from the property's set.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
