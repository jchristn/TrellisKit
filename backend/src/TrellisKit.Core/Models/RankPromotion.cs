namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// One rank award or revoke (table rankpromotions, id prefix rkp_). Award rows carry revokedUtc once revoked; revoke rows name the award in awardId.
    /// </summary>
    public class RankPromotion : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (rkp_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Member whose rank changed.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Key of the Rank profile field.
        /// </summary>
        public string FieldKey { get; set; } = String.Empty;

        /// <summary>
        /// Option key.
        /// </summary>
        public string OptionKey { get; set; } = String.Empty;

        /// <summary>
        /// Level within the option.
        /// </summary>
        public int Level { get; set; } = 0;

        /// <summary>
        /// Award or Revoke.
        /// </summary>
        public RankPromotionActionEnum Action { get; set; } = RankPromotionActionEnum.Award;

        /// <summary>
        /// Awarder or revoker member identifier, or null for the admin token.
        /// </summary>
        public string? ActorId { get; set; } = null;

        /// <summary>
        /// Awarder's note (at most 500 characters).
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Optional event the award happened at (for example a workshop).
        /// </summary>
        public string? EventId { get; set; } = null;

        /// <summary>
        /// Revoke reason (revoke rows).
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Revoke rows: the award that was revoked.
        /// </summary>
        public string? AwardId { get; set; } = null;

        /// <summary>
        /// Award rows: when the award was revoked, or null while it stands.
        /// </summary>
        public DateTime? RevokedUtc { get; set; } = null;

        /// <summary>
        /// Creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update time.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
