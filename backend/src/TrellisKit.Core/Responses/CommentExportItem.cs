namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A comment in the account export (contract section 11), with its revisions.
    /// </summary>
    public class CommentExportItem
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Target type.
        /// </summary>
        public string TargetType { get; set; } = String.Empty;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Parent.
        /// </summary>
        public string? ParentCommentId { get; set; } = null;

        /// <summary>
        /// Body (null once erased).
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Edited.
        /// </summary>
        public DateTime? EditedUtc { get; set; } = null;

        /// <summary>
        /// Deleted.
        /// </summary>
        public DateTime? DeletedUtc { get; set; } = null;

        /// <summary>
        /// Revisions, oldest first (never null).
        /// </summary>
        public List<CommentRevisionView> Revisions
        {
            get { return _Revisions; }
            set { _Revisions = value ?? new List<CommentRevisionView>(); }
        }

        #endregion

        #region Private-Members

        private List<CommentRevisionView> _Revisions = new List<CommentRevisionView>();

        #endregion
    }
}
