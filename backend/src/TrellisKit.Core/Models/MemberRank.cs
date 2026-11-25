namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// A member's current verified rank in one Rank profile field (table memberranks, id prefix mrk_). One row per member and field.
    /// </summary>
    public class MemberRank : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (mrk_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Key of the Rank profile field.
        /// </summary>
        public string FieldKey { get; set; } = String.Empty;

        /// <summary>
        /// Awarded option key.
        /// </summary>
        public string OptionKey { get; set; } = String.Empty;

        /// <summary>
        /// Awarded level within the option (0 to the option's maxLevel).
        /// </summary>
        public int Level { get; set; } = 0;

        /// <summary>
        /// Verified, Revoked, or Disputed. Only Verified rows are the effective rank.
        /// </summary>
        public RankStatusEnum Status { get; set; } = RankStatusEnum.Verified;

        /// <summary>
        /// The award (rankpromotions row) this rank came from.
        /// </summary>
        public string? PromotionId { get; set; } = null;

        /// <summary>
        /// Awarder member identifier, or null for the admin token.
        /// </summary>
        public string? AwardedBy { get; set; } = null;

        /// <summary>
        /// When the rank was awarded (time in grade starts here).
        /// </summary>
        public DateTime AwardedUtc { get; set; } = DateTime.UtcNow;

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
