namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A venue review (docs/PHASE4_CONTRACT.md section 3.12).
    /// </summary>
    public class VenueReviewView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (vrv_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Venue.
        /// </summary>
        public string VenueId { get; set; } = String.Empty;

        /// <summary>
        /// Author.
        /// </summary>
        public UserSummary? Author { get; set; } = null;

        /// <summary>
        /// Star rating 1 to 5.
        /// </summary>
        public int Rating { get; set; } = 0;

        /// <summary>
        /// Review text.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// When the author last edited it, or null.
        /// </summary>
        public DateTime? EditedUtc { get; set; } = null;

        /// <summary>
        /// The instructor reply, or null.
        /// </summary>
        public VenueReviewReplyView? Reply { get; set; } = null;

        /// <summary>
        /// Moderation state (None, Hidden, Removed).
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
        public VenueReviewViewer Viewer { get; set; } = new VenueReviewViewer();

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
