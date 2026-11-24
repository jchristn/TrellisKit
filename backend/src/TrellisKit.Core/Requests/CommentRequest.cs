namespace TrellisKit.Core.Requests
{
    using System.Collections.Generic;

    /// <summary>
    /// Body of POST /api/v1/feed/{id}/comments and PUT /api/v1/comments/{id} (contract sections 7.5 and 7.6). On PUT, omitted fields stay unchanged.
    /// </summary>
    public class CommentRequest
    {
        #region Public-Members

        /// <summary>
        /// Markdown body (section 3; headings render as plain text).
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Parent comment for a reply (create only).
        /// </summary>
        public string? ParentCommentId { get; set; } = null;

        /// <summary>
        /// Media (at most comments.maxAttachments); on PUT replaces the list.
        /// </summary>
        public List<string>? MediaIds { get; set; } = null;

        /// <summary>
        /// Whether to build a preview card (default true).
        /// </summary>
        public bool? ShowLinkPreview { get; set; } = null;

        /// <summary>
        /// Labels (replace semantics on PUT; null keeps them).
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags (replace semantics on PUT; null keeps them).
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
