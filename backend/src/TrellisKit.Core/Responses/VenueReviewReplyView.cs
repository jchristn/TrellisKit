namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// The public instructor reply of a venue review (docs/PHASE4_CONTRACT.md section 3.12).
    /// </summary>
    public class VenueReviewReplyView
    {
        #region Public-Members

        /// <summary>
        /// Reply text.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Member who replied.
        /// </summary>
        public UserSummary? Author { get; set; } = null;

        /// <summary>
        /// Instructor entry the reply was written as.
        /// </summary>
        public string? InstructorId { get; set; } = null;

        /// <summary>
        /// Display name of that instructor entry.
        /// </summary>
        public string? InstructorName { get; set; } = null;

        /// <summary>
        /// When the reply was first written.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the reply was last edited, or null.
        /// </summary>
        public DateTime? EditedUtc { get; set; } = null;

        #endregion
    }
}
