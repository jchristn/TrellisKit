namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A stored earlier version of a comment (contract section 7.6).
    /// </summary>
    public class CommentRevision
    {
        #region Public-Members

        /// <summary>
        /// Identifier (cmr_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Comment.
        /// </summary>
        public string CommentId { get; set; } = String.Empty;

        /// <summary>
        /// Revision number, from 1.
        /// </summary>
        public int Revision { get; set; } = 0;

        /// <summary>
        /// Previous body.
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Attachment change text, or null.
        /// </summary>
        public string? AttachmentSummary { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
