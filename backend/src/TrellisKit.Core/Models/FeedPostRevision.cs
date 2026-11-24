namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A stored earlier version of a feed post (contract section 5.5).
    /// </summary>
    public class FeedPostRevision
    {
        #region Public-Members

        /// <summary>
        /// Identifier (fpr_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Post.
        /// </summary>
        public string FeedPostId { get; set; } = String.Empty;

        /// <summary>
        /// Revision number, from 1.
        /// </summary>
        public int Revision { get; set; } = 0;

        /// <summary>
        /// Previous title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Previous body.
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Attachment change text, for example "Removed 1 image, added 1 video", or null.
        /// </summary>
        public string? AttachmentSummary { get; set; } = null;

        /// <summary>
        /// Who made the edit that replaced this version, or null for the admin token.
        /// </summary>
        public string? EditedByUserId { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
