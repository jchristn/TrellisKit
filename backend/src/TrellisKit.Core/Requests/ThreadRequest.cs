namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Create or edit a thread (POST /api/v1/forums/{id}/threads and PUT /api/v1/threads/{id}).
    /// </summary>
    public class ThreadRequest
    {
        #region Public-Members

        /// <summary>
        /// Thread type (required on create; moderators may swap Discussion and Question on edit).
        /// </summary>
        public ThreadTypeEnum? Type { get; set; } = null;

        /// <summary>
        /// Title (3 to 140 characters).
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Body (Markdown subset).
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Attachments in order (replaces the list on edit).
        /// </summary>
        public List<string>? MediaIds { get; set; } = null;

        /// <summary>
        /// Whether to show a link preview (default true).
        /// </summary>
        public bool? ShowLinkPreview { get; set; } = null;

        /// <summary>
        /// Audience (not broader than the forum's).
        /// </summary>
        public ContentAudienceEnum? Audience { get; set; } = null;

        /// <summary>
        /// Poll (Poll threads).
        /// </summary>
        public PollRequest? Poll { get; set; } = null;

        /// <summary>
        /// Feedback focus (Feedback threads).
        /// </summary>
        public FeedbackRequest? Feedback { get; set; } = null;

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
