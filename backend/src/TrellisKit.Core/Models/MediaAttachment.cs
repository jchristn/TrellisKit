namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One uploaded image or video (table mediaattachments): upload session, processing state, stored objects, and
    /// the content it is attached to.
    /// </summary>
    public class MediaAttachment
    {
        #region Public-Members

        /// <summary>
        /// Identifier (med_ prefix).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Member who uploaded the media.
        /// </summary>
        public string OwnerUserId { get; set; } = String.Empty;

        /// <summary>
        /// Image or video.
        /// </summary>
        public MediaKindEnum Kind { get; set; } = MediaKindEnum.Image;

        /// <summary>
        /// Upload and processing status.
        /// </summary>
        public MediaStatusEnum Status { get; set; } = MediaStatusEnum.Uploading;

        /// <summary>
        /// Failure reason when Failed, otherwise null.
        /// </summary>
        public MediaFailureReasonEnum? FailureReason { get; set; } = null;

        /// <summary>
        /// Declared content type, for example image/jpeg.
        /// </summary>
        public string ContentType { get; set; } = String.Empty;

        /// <summary>
        /// Original file name from the client, kept for the admin library only, or null.
        /// </summary>
        public string? OriginalFileName { get; set; } = null;

        /// <summary>
        /// Declared (and after complete, confirmed) size of the original in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// Video duration declared by the client at session creation, or null.
        /// </summary>
        public double? DeclaredDurationSeconds { get; set; } = null;

        /// <summary>
        /// Video duration read from the container, or null until known.
        /// </summary>
        public double? DurationSeconds { get; set; } = null;

        /// <summary>
        /// Display width after orientation, or null until Ready.
        /// </summary>
        public int? Width { get; set; } = null;

        /// <summary>
        /// Display height after orientation, or null until Ready.
        /// </summary>
        public int? Height { get; set; } = null;

        /// <summary>
        /// Alternative text, or null.
        /// </summary>
        public string? AltText { get; set; } = null;

        /// <summary>
        /// Blurhash placeholder, or null until Ready.
        /// </summary>
        public string? Blurhash { get; set; } = null;

        /// <summary>
        /// Storage mode the objects were written with.
        /// </summary>
        public MediaStorageModeEnum StorageMode { get; set; } = MediaStorageModeEnum.Local;

        /// <summary>
        /// Bucket (S3) or folder (Local) of the original, or null.
        /// </summary>
        public string? SourceBucket { get; set; } = null;

        /// <summary>
        /// Object key of the original (kept), for example in/med_x/original.
        /// </summary>
        public string SourceKey { get; set; } = String.Empty;

        /// <summary>
        /// Multipart upload identifier while a multipart session is open, or null.
        /// </summary>
        public string? MultipartUploadId { get; set; } = null;

        /// <summary>
        /// Part size for multipart sessions, 0 for single uploads.
        /// </summary>
        public long PartSizeBytes { get; set; } = 0;

        /// <summary>
        /// Part count for multipart sessions, 0 for single uploads.
        /// </summary>
        public int PartCount { get; set; } = 0;

        /// <summary>
        /// Key prefix of processed outputs, or null.
        /// </summary>
        public string? OutputPrefix { get; set; } = null;

        /// <summary>
        /// HLS master playlist file name (in the transcoded location under video/{id}/), or null.
        /// </summary>
        public string? HlsManifestFile { get; set; } = null;

        /// <summary>
        /// Passthrough MP4 file name (in the transcoded location under video/{id}/), or null.
        /// </summary>
        public string? Mp4File { get; set; } = null;

        /// <summary>
        /// Poster file name (in the images location under media/{id}/), or null.
        /// </summary>
        public string? PosterFile { get; set; } = null;

        /// <summary>
        /// Poster thumbnail file name (in the images location under media/{id}/), or null.
        /// </summary>
        public string? PosterThumbFile { get; set; } = null;

        /// <summary>
        /// Whether the served objects were moved to the retained location (moderator removal).
        /// </summary>
        public bool IsRetained { get; set; } = false;

        /// <summary>
        /// Whether the served objects were copied to the public paths.
        /// </summary>
        public bool IsPublic { get; set; } = false;

        /// <summary>
        /// Processor label (skia, mediaconvert, ffmpeg, passthrough), or null.
        /// </summary>
        public string? Processor { get; set; } = null;

        /// <summary>
        /// Asynchronous transcode job identifier, or null.
        /// </summary>
        public string? ProcessorJobId { get; set; } = null;

        /// <summary>
        /// Processing attempts so far.
        /// </summary>
        public int Attempts { get; set; } = 0;

        /// <summary>
        /// Worker instance holding the processing claim, or null.
        /// </summary>
        public string? ClaimedBy { get; set; } = null;

        /// <summary>
        /// End of the processing claim, or null.
        /// </summary>
        public DateTime? ClaimUntilUtc { get; set; } = null;

        /// <summary>
        /// When processing first started, or null.
        /// </summary>
        public DateTime? ProcessingStartedUtc { get; set; } = null;

        /// <summary>
        /// When the media became Ready, or null.
        /// </summary>
        public DateTime? ReadyUtc { get; set; } = null;

        /// <summary>
        /// When the upload session expires.
        /// </summary>
        public DateTime UploadExpiresUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Type of the content the media is attached to, or null.
        /// </summary>
        public TargetTypeEnum? AttachedTargetType { get; set; } = null;

        /// <summary>
        /// Identifier of the content the media is attached to, or null.
        /// </summary>
        public string? AttachedTargetId { get; set; } = null;

        /// <summary>
        /// Zero-based position within the target's attachments.
        /// </summary>
        public int AttachedPosition { get; set; } = 0;

        /// <summary>
        /// When the media was attached, or null.
        /// </summary>
        public DateTime? AttachedUtc { get; set; } = null;

        /// <summary>
        /// When the media was last detached from content (by an edit), or null.
        /// </summary>
        public DateTime? DetachedUtc { get; set; } = null;

        /// <summary>
        /// When the media was deleted, or null.
        /// </summary>
        public DateTime? DeletedUtc { get; set; } = null;

        /// <summary>
        /// Purpose (docs/PHASE5_CONTRACT.md sections 3.7 and 6.1): Content (default, served through the media grant) or a
        /// private purpose (Ticket, Message) served only through per-object tokens.
        /// </summary>
        public MediaPurposeEnum Purpose { get; set; } = MediaPurposeEnum.Content;

        /// <summary>
        /// Scope of a private purpose (the ticket or conversation identifier), or null while pending (a private upload
        /// not yet attached) and for Content media.
        /// </summary>
        public string? ScopeId { get; set; } = null;

        /// <summary>
        /// Whether the media has a private purpose (never readable through the member-wide media grant).
        /// </summary>
        public bool IsPrivate
        {
            get { return Purpose != MediaPurposeEnum.Content; }
        }

        /// <summary>
        /// Creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last change.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Whether the media is attached to content.
        /// </summary>
        public bool IsAttached
        {
            get { return AttachedTargetType != null && !String.IsNullOrEmpty(AttachedTargetId); }
        }

        /// <summary>
        /// Whether the media was deleted.
        /// </summary>
        public bool IsDeleted
        {
            get { return DeletedUtc != null; }
        }

        /// <summary>
        /// Location of the original: Source for images, VideoSource for videos.
        /// </summary>
        public MediaLocationEnum SourceLocation
        {
            get { return Kind == MediaKindEnum.Video ? MediaLocationEnum.VideoSource : MediaLocationEnum.Source; }
        }

        #endregion
    }
}
