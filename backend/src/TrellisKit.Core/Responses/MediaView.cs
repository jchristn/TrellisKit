namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The Media payload (contract section 2), with labels and tags (target type Media).
    /// </summary>
    public class MediaView
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Image or Video.
        /// </summary>
        public MediaKindEnum Kind { get; set; } = MediaKindEnum.Image;

        /// <summary>
        /// Status.
        /// </summary>
        public MediaStatusEnum Status { get; set; } = MediaStatusEnum.Uploading;

        /// <summary>
        /// Failure reason when Failed, otherwise null.
        /// </summary>
        public MediaFailureReasonEnum? FailureReason { get; set; } = null;

        /// <summary>
        /// Owner (the uploading member). Null, and omitted from the JSON, on anonymous and public payloads
        /// (docs/PHASE4_CONTRACT.md section 5.12 and docs/PHASE2_CONTRACT.md section 5.10), which never name the uploader.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? OwnerUserId { get; set; } = String.Empty;

        /// <summary>
        /// Declared content type.
        /// </summary>
        public string ContentType { get; set; } = String.Empty;

        /// <summary>
        /// Size of the original in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// Display width, null until Ready.
        /// </summary>
        public int? Width { get; set; } = null;

        /// <summary>
        /// Display height, null until Ready.
        /// </summary>
        public int? Height { get; set; } = null;

        /// <summary>
        /// Video duration, null until known.
        /// </summary>
        public double? DurationSeconds { get; set; } = null;

        /// <summary>
        /// Alternative text, or null.
        /// </summary>
        public string? AltText { get; set; } = null;

        /// <summary>
        /// Blurhash placeholder, null until Ready.
        /// </summary>
        public string? Blurhash { get; set; } = null;

        /// <summary>
        /// Image fields when Ready, otherwise null.
        /// </summary>
        public MediaImageView? Image { get; set; } = null;

        /// <summary>
        /// Video fields when Ready, otherwise null.
        /// </summary>
        public MediaVideoView? Video { get; set; } = null;

        /// <summary>
        /// Document file (Document media that are Ready), or null.
        /// </summary>
        public MediaDocumentView? Document { get; set; } = null;

        /// <summary>
        /// Creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the media became Ready, or null.
        /// </summary>
        public DateTime? ReadyUtc { get; set; } = null;

        /// <summary>
        /// Labels (never null).
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags (never null).
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
