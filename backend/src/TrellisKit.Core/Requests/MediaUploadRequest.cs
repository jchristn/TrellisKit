namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of POST /api/v1/media/uploads (contract section 4.2).
    /// </summary>
    public class MediaUploadRequest
    {
        #region Public-Members

        /// <summary>
        /// Image or Video.
        /// </summary>
        public string? Kind { get; set; } = null;

        /// <summary>
        /// Content type.
        /// </summary>
        public string? ContentType { get; set; } = null;

        /// <summary>
        /// Size in bytes.
        /// </summary>
        public long? SizeBytes { get; set; } = null;

        /// <summary>
        /// Original file name (up to 255 characters).
        /// </summary>
        public string? FileName { get; set; } = null;

        /// <summary>
        /// Declared duration (required for video).
        /// </summary>
        public double? DurationSeconds { get; set; } = null;

        /// <summary>
        /// Purpose: Content (default), Ticket, or Message (docs/PHASE5_CONTRACT.md sections 3.7 and 6.1). Private purposes
        /// are never served through the media grant.
        /// </summary>
        public string? Purpose { get; set; } = null;

        /// <summary>
        /// Scope of a private purpose (ticket or conversation identifier), or null to attach it to a new ticket later
        /// (pending until the first attachment binds it).
        /// </summary>
        public string? ScopeId { get; set; } = null;

        /// <summary>
        /// Optional labels.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Optional tags.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
