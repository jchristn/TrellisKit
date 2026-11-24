namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Body of POST and PUT /api/v1/feed (contract sections 5.4 and 5.5) and of the admin feed routes (10.1). On PUT, omitted fields stay unchanged.
    /// </summary>
    public class FeedPostRequest
    {
        #region Public-Members

        /// <summary>
        /// Optional title (up to 140 characters).
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Markdown body (section 3).
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Media in display order; on PUT replaces the list.
        /// </summary>
        public List<string>? MediaIds { get; set; } = null;

        /// <summary>
        /// Members (default) or Public (SystemAdmin only).
        /// </summary>
        public FeedVisibilityEnum? Visibility { get; set; } = null;

        /// <summary>
        /// Audience (Public, SignedIn, Members); Public needs SystemAdmin. When only visibility is sent the audience follows it.
        /// </summary>
        public ContentAudienceEnum? Audience { get; set; } = null;

        /// <summary>
        /// Whether comments are allowed (default true).
        /// </summary>
        public bool? CommentsEnabled { get; set; } = null;

        /// <summary>
        /// Scheduled publish time (1 minute to 365 days ahead), or null to publish now.
        /// </summary>
        public DateTime? PublishAtUtc { get; set; } = null;

        /// <summary>
        /// Pin on create (SystemAdmin only; ignored for others).
        /// </summary>
        public bool? Pin { get; set; } = null;

        /// <summary>
        /// Whether to build a preview card (default true).
        /// </summary>
        public bool? ShowLinkPreview { get; set; } = null;

        /// <summary>
        /// Labels (replace semantics on PUT; null keeps them).
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags (replace semantics on PUT; null keeps them).
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        /// <summary>
        /// Admin create only: author (must have canPostToFeed or SystemAdmin); default the caller.
        /// </summary>
        public string? AuthorUserId { get; set; } = null;

        #endregion
    }
}
