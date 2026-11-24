namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Create or edit an instructor (docs/PHASE4_CONTRACT.md sections 2.3 and 3.9). Omitted fields stay on edit.
    /// </summary>
    public class InstructorRequest
    {
        #region Public-Members

        /// <summary>
        /// Display name (2 to 120).
        /// </summary>
        public string? DisplayName { get; set; } = null;

        /// <summary>
        /// Slug (derived from the name when omitted on create).
        /// </summary>
        public string? Slug { get; set; } = null;

        /// <summary>
        /// Bio (0 to directory.maxDescriptionLength).
        /// </summary>
        public string? Bio { get; set; } = null;

        /// <summary>
        /// Ready image media id, or null.
        /// </summary>
        public string? PhotoMediaId { get; set; } = null;

        /// <summary>
        /// Option key of the configured Rank profile field.
        /// </summary>
        public string? Rank { get; set; } = null;

        /// <summary>
        /// Level within the option (0 to its maxLevel).
        /// </summary>
        public int? RankLevel { get; set; } = null;

        /// <summary>
        /// Affiliation (0 to 120).
        /// </summary>
        public string? Affiliation { get; set; } = null;

        /// <summary>
        /// Website (https).
        /// </summary>
        public string? WebsiteUrl { get; set; } = null;

        /// <summary>
        /// Social links.
        /// </summary>
        public List<SocialLinkRequest>? SocialLinks { get; set; } = null;

        /// <summary>
        /// Venues to link (create only; Regular role).
        /// </summary>
        public List<string>? VenueIds { get; set; } = null;

        /// <summary>
        /// Audience (Public only by SystemAdmin).
        /// </summary>
        public ContentAudienceEnum? Audience { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
