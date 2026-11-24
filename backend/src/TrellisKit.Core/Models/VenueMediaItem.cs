namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A photo or video a member added to a venue's Media section (docs/PHASE4_CONTRACT.md section 3.13). The media
    /// row is attached to this item (target type VenueMedia).
    /// </summary>
    public class VenueMediaItem
    {
        #region Public-Members

        /// <summary>
        /// Identifier (vmd_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Venue.
        /// </summary>
        public string VenueId { get; set; } = String.Empty;

        /// <summary>
        /// Media identifier.
        /// </summary>
        public string MediaId { get; set; } = String.Empty;

        /// <summary>
        /// Uploader.
        /// </summary>
        public string UploaderUserId { get; set; } = String.Empty;

        /// <summary>
        /// Media kind (Image or Video).
        /// </summary>
        public MediaKindEnum Kind { get; set; } = MediaKindEnum.Image;

        /// <summary>
        /// Open reports.
        /// </summary>
        public int OpenReportCount { get; set; } = 0;

        /// <summary>
        /// Moderation state (Removed after a staff or instructor removal).
        /// </summary>
        public ModerationStateEnum ModerationState { get; set; } = ModerationStateEnum.None;

        /// <summary>
        /// When the moderation state last changed, or null.
        /// </summary>
        public DateTime? ModeratedUtc { get; set; } = null;

        /// <summary>
        /// Moderator of the last change, or null.
        /// </summary>
        public string? ModeratedByUserId { get; set; } = null;

        /// <summary>
        /// Reason of the last moderation change, or null.
        /// </summary>
        public string? ModerationReason { get; set; } = null;

        /// <summary>
        /// Deletion time (the uploader deleted it), or null.
        /// </summary>
        public DateTime? DeletedUtc { get; set; } = null;

        /// <summary>
        /// Creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update time.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Whether the item is shown to members (not deleted, not moderated).
        /// </summary>
        public bool IsLive
        {
            get { return DeletedUtc == null && ModerationState == ModerationStateEnum.None; }
        }

        #endregion
    }
}
