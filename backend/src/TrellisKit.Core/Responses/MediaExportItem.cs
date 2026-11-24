namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Account export item of a media asset (metadata only, no URLs or storage keys).
    /// </summary>
    public class MediaExportItem
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Image or Video.
        /// </summary>
        public string Kind { get; set; } = String.Empty;

        /// <summary>
        /// Status.
        /// </summary>
        public string Status { get; set; } = String.Empty;

        /// <summary>
        /// Content type.
        /// </summary>
        public string ContentType { get; set; } = String.Empty;

        /// <summary>
        /// Original file name.
        /// </summary>
        public string? OriginalFileName { get; set; } = null;

        /// <summary>
        /// Size.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// Width.
        /// </summary>
        public int? Width { get; set; } = null;

        /// <summary>
        /// Height.
        /// </summary>
        public int? Height { get; set; } = null;

        /// <summary>
        /// Duration of a video.
        /// </summary>
        public double? DurationSeconds { get; set; } = null;

        /// <summary>
        /// Alt text.
        /// </summary>
        public string? AltText { get; set; } = null;

        /// <summary>
        /// Attached target type.
        /// </summary>
        public string? AttachedTargetType { get; set; } = null;

        /// <summary>
        /// Attached target.
        /// </summary>
        public string? AttachedTargetId { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Deleted (UTC).
        /// </summary>
        public DateTime? DeletedUtc { get; set; } = null;

        #endregion
    }
}
