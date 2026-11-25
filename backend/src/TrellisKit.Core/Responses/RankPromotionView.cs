namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One rank award or revoke with member summaries.
    /// </summary>
    public class RankPromotionView : IMetadataPayload
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
        /// Member summary.
        /// </summary>
        public UserSummary? User { get; set; } = null;

        /// <summary>
        /// Rank field key.
        /// </summary>
        public string FieldKey { get; set; } = String.Empty;

        /// <summary>
        /// Option key.
        /// </summary>
        public string OptionKey { get; set; } = String.Empty;

        /// <summary>
        /// Option label.
        /// </summary>
        public string? OptionLabel { get; set; } = null;

        /// <summary>
        /// Level.
        /// </summary>
        public int Level { get; set; } = 0;

        /// <summary>
        /// Display text, for example Gold, 2 Stars.
        /// </summary>
        public string? RankLabel { get; set; } = null;

        /// <summary>
        /// Award or Revoke.
        /// </summary>
        public RankPromotionActionEnum Action { get; set; } = RankPromotionActionEnum.Award;

        /// <summary>
        /// Awarder or revoker, or null for the admin token.
        /// </summary>
        public string? ActorId { get; set; } = null;

        /// <summary>
        /// Actor summary.
        /// </summary>
        public UserSummary? Actor { get; set; } = null;

        /// <summary>
        /// Note.
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Event, or null.
        /// </summary>
        public string? EventId { get; set; } = null;

        /// <summary>
        /// Revoke reason.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Revoke rows: the revoked award.
        /// </summary>
        public string? AwardId { get; set; } = null;

        /// <summary>
        /// Award rows: revocation time, or null while it stands.
        /// </summary>
        public DateTime? RevokedUtc { get; set; } = null;

        /// <summary>
        /// Time of the award or revoke.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

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
