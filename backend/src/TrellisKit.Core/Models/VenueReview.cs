namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A member's review of a venue with the optional instructor reply (docs/PHASE4_CONTRACT.md section 3.12).
    /// </summary>
    public class VenueReview
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
        public string AuthorUserId { get; set; } = String.Empty;

        /// <summary>
        /// Star rating. Minimum 1, maximum 5.
        /// </summary>
        public int Rating
        {
            get { return _Rating; }
            set { _Rating = Math.Clamp(value, 1, 5); }
        }

        /// <summary>
        /// Review text.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Instructor reply text, or null.
        /// </summary>
        public string? ReplyBody { get; set; } = null;

        /// <summary>
        /// Member who wrote the reply, or null.
        /// </summary>
        public string? ReplyUserId { get; set; } = null;

        /// <summary>
        /// Instructor entry the reply was written as, or null.
        /// </summary>
        public string? ReplyInstructorId { get; set; } = null;

        /// <summary>
        /// When the reply was first written, or null.
        /// </summary>
        public DateTime? ReplyUtc { get; set; } = null;

        /// <summary>
        /// When the reply was last edited, or null.
        /// </summary>
        public DateTime? ReplyEditedUtc { get; set; } = null;

        /// <summary>
        /// When the author last edited the review, or null.
        /// </summary>
        public DateTime? EditedUtc { get; set; } = null;

        /// <summary>
        /// Open reports.
        /// </summary>
        public int OpenReportCount { get; set; } = 0;

        /// <summary>
        /// Moderation state.
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
        /// Deletion time, or null.
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
        /// Whether the review counts toward the venue's rating (not deleted, not moderated).
        /// </summary>
        public bool IsLive
        {
            get { return DeletedUtc == null && ModerationState == ModerationStateEnum.None; }
        }

        #endregion

        #region Private-Members

        private int _Rating = 5;

        #endregion
    }
}
