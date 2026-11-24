namespace TrellisKit.Core.Responses
{
    /// <summary>
    /// Capabilities of the caller on a comment (contract section 2; canReply in place of canComment, no canPin). Every flag is false on placeholders.
    /// </summary>
    public class CommentViewer
    {
        #region Public-Members

        /// <summary>
        /// Author with posting rights.
        /// </summary>
        public bool CanEdit { get; set; } = false;

        /// <summary>
        /// Author or SystemAdmin.
        /// </summary>
        public bool CanDelete { get; set; } = false;

        /// <summary>
        /// The post accepts comments, caller may write and is not blocked by the comment's author.
        /// </summary>
        public bool CanReply { get; set; } = false;

        /// <summary>
        /// Caller is not muted and not blocked by the author.
        /// </summary>
        public bool CanReact { get; set; } = false;

        /// <summary>
        /// Any member on someone else's comment.
        /// </summary>
        public bool CanReport { get; set; } = false;

        /// <summary>
        /// SystemAdmin, or a FeedModerator on a comment not authored by a SystemAdmin.
        /// </summary>
        public bool CanModerate { get; set; } = false;

        /// <summary>
        /// Caller may attach images.
        /// </summary>
        public bool CanAttachMedia { get; set; } = false;

        /// <summary>
        /// Caller may attach video.
        /// </summary>
        public bool CanAttachVideo { get; set; } = false;

        #endregion
    }
}
