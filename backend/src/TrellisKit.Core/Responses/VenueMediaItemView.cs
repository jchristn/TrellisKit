namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A photo or video a member added to a venue (docs/PHASE4_CONTRACT.md section 3.13).
    /// </summary>
    public class VenueMediaItemView : IMetadataPayload
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
        /// The media (variants, poster, HLS).
        /// </summary>
        public MediaView? Media { get; set; } = null;

        /// <summary>
        /// Uploader.
        /// </summary>
        public UserSummary? Uploader { get; set; } = null;

        /// <summary>
        /// Moderation state.
        /// </summary>
        public ModerationStateEnum ModerationState { get; set; } = ModerationStateEnum.None;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Caller capabilities.
        /// </summary>
        public VenueMediaItemViewer Viewer { get; set; } = new VenueMediaItemViewer();

        /// <summary>
        /// Creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update time.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
