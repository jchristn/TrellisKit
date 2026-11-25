namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// Filters of the rank awards enumeration.
    /// </summary>
    public class RankPromotionFilter
    {
        #region Public-Members

        /// <summary>
        /// Member.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Rank field key.
        /// </summary>
        public string? FieldKey { get; set; } = null;

        /// <summary>
        /// Option key.
        /// </summary>
        public string? OptionKey { get; set; } = null;

        /// <summary>
        /// Awarder or revoker.
        /// </summary>
        public string? ActorId { get; set; } = null;

        /// <summary>
        /// Award or Revoke.
        /// </summary>
        public TrellisKit.Core.Enums.RankPromotionActionEnum? Action { get; set; } = null;

        /// <summary>
        /// Award rows only: true for revoked awards, false for standing awards.
        /// </summary>
        public bool? Revoked { get; set; } = null;

        /// <summary>
        /// Created at or after.
        /// </summary>
        public DateTime? FromUtc { get; set; } = null;

        /// <summary>
        /// Created before.
        /// </summary>
        public DateTime? ToUtc { get; set; } = null;

        #endregion
    }
}
