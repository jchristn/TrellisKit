namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A directory instructor row (docs/PHASE4_CONTRACT.md sections 2.3 and 14).
    /// </summary>
    public class Instructor
    {
        #region Public-Members

        /// <summary>
        /// Identifier (ins_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// URL slug, unique per property among live instructors.
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Display name (2 to 120 characters).
        /// </summary>
        public string DisplayName { get; set; } = String.Empty;

        /// <summary>
        /// Bio in the Markdown subset, or null.
        /// </summary>
        public string? Bio { get; set; } = null;

        /// <summary>
        /// Photo media id, or null.
        /// </summary>
        public string? PhotoMediaId { get; set; } = null;

        /// <summary>
        /// Option key of the configured Rank profile field, or null.
        /// </summary>
        public string? RankKey { get; set; } = null;

        /// <summary>
        /// Level within the rank option, or null.
        /// </summary>
        public int? RankLevel { get; set; } = null;

        /// <summary>
        /// Affiliation (0 to 120), or null.
        /// </summary>
        public string? Affiliation { get; set; } = null;

        /// <summary>
        /// Linked member account (claimed), or null.
        /// </summary>
        public string? LinkedUserId { get; set; } = null;

        /// <summary>
        /// When the account was linked, or null.
        /// </summary>
        public DateTime? LinkedUtc { get; set; } = null;

        /// <summary>
        /// Website (https), or null.
        /// </summary>
        public string? WebsiteUrl { get; set; } = null;

        /// <summary>
        /// Lifecycle status.
        /// </summary>
        public InstructorStatusEnum Status { get; set; } = InstructorStatusEnum.Active;

        /// <summary>
        /// Who may read it.
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// Linked venues (recomputed).
        /// </summary>
        public int VenueCount { get; set; } = 0;

        /// <summary>
        /// Open reports.
        /// </summary>
        public int OpenReportCount { get; set; } = 0;

        /// <summary>
        /// Moderation state.
        /// </summary>
        public ModerationStateEnum ModerationState { get; set; } = ModerationStateEnum.None;

        /// <summary>
        /// When it was last moderated, or null.
        /// </summary>
        public DateTime? ModeratedUtc { get; set; } = null;

        /// <summary>
        /// Moderator, or null.
        /// </summary>
        public string? ModeratedByUserId { get; set; } = null;

        /// <summary>
        /// Moderation reason, or null.
        /// </summary>
        public string? ModerationReason { get; set; } = null;

        /// <summary>
        /// Creator, or null.
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// Soft delete time, or null.
        /// </summary>
        public DateTime? DeletedUtc { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last change (the stale check of submissions compares it).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Social links in display order.
        /// </summary>
        public List<DirectorySocialLink> SocialLinks { get; set; } = new List<DirectorySocialLink>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// A deep copy (child lists copied).
        /// </summary>
        /// <returns>Copy.</returns>
        public Instructor Clone()
        {
            Instructor copy = (Instructor)MemberwiseClone();
            copy.SocialLinks = SocialLinks.Select(l => new DirectorySocialLink { Id = l.Id, Kind = l.Kind, Url = l.Url, Position = l.Position }).ToList();
            return copy;
        }

        #endregion
    }
}
