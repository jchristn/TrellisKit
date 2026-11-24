namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An instructor list item (docs/PHASE4_CONTRACT.md section 2.3): the Instructor without bio, venues, and social links.
    /// </summary>
    public class InstructorSummaryView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Display name.
        /// </summary>
        public string DisplayName { get; set; } = String.Empty;

        /// <summary>
        /// Current slug.
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Photo, or null.
        /// </summary>
        public MediaView? Photo { get; set; } = null;

        /// <summary>
        /// Rank option key (only when directory.instructorRankFieldKey is set).
        /// </summary>
        public string? Rank { get; set; } = null;

        /// <summary>
        /// Rank option label.
        /// </summary>
        public string? RankLabel { get; set; } = null;

        /// <summary>
        /// Rank option color.
        /// </summary>
        public string? RankColor { get; set; } = null;

        /// <summary>
        /// Rank level.
        /// </summary>
        public int? RankLevel { get; set; } = null;

        /// <summary>
        /// Affiliation.
        /// </summary>
        public string? Affiliation { get; set; } = null;

        /// <summary>
        /// Linked member, or null (omitted on public routes).
        /// </summary>
        public UserSummary? LinkedUser { get; set; } = null;

        /// <summary>
        /// Linked venues.
        /// </summary>
        public int VenueCount { get; set; } = 0;

        /// <summary>
        /// Website.
        /// </summary>
        public string? WebsiteUrl { get; set; } = null;

        /// <summary>
        /// Role at the venue (venue instructor lists only).
        /// </summary>
        public VenueInstructorRoleEnum? Role { get; set; } = null;

        /// <summary>
        /// Status.
        /// </summary>
        public InstructorStatusEnum Status { get; set; } = InstructorStatusEnum.Active;

        /// <summary>
        /// Audience.
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// Locked stub.
        /// </summary>
        public bool Locked { get; set; } = false;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Caller capabilities (null on public routes).
        /// </summary>
        public InstructorViewer? Viewer { get; set; } = new InstructorViewer();

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
