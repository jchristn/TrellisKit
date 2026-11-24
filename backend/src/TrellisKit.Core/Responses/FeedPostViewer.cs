namespace TrellisKit.Core.Responses
{
    /// <summary>
    /// Capabilities of the caller on a feed post (contract section 2, Viewer capabilities). The server still enforces everything.
    /// </summary>
    public class FeedPostViewer
    {
        #region Public-Members

        /// <summary>
        /// Author with posting rights, or SystemAdmin.
        /// </summary>
        public bool CanEdit { get; set; } = false;

        /// <summary>
        /// Whether the caller may open the post (false on a locked stub). Default true.
        /// </summary>
        public bool CanOpen { get; set; } = true;

        /// <summary>
        /// What opening a locked stub requires (Membership), or null when the post can be opened.
        /// </summary>
        public string? Requires { get; set; } = null;

        /// <summary>
        /// Author or SystemAdmin.
        /// </summary>
        public bool CanDelete { get; set; } = false;

        /// <summary>
        /// SystemAdmin on a Published post.
        /// </summary>
        public bool CanPin { get; set; } = false;

        /// <summary>
        /// Published, comments enabled, caller may write and is not blocked by the author.
        /// </summary>
        public bool CanComment { get; set; } = false;

        /// <summary>
        /// Published, caller is not muted and not blocked by the author.
        /// </summary>
        public bool CanReact { get; set; } = false;

        /// <summary>
        /// Any member on someone else's post.
        /// </summary>
        public bool CanReport { get; set; } = false;

        /// <summary>
        /// SystemAdmin, or a FeedModerator on a post not authored by a SystemAdmin.
        /// </summary>
        public bool CanModerate { get; set; } = false;

        /// <summary>
        /// Caller may attach images (canPost, canPostMedia, not muted).
        /// </summary>
        public bool CanAttachMedia { get; set; } = false;

        /// <summary>
        /// Caller may attach video (also canUploadVideo).
        /// </summary>
        public bool CanAttachVideo { get; set; } = false;

        #endregion
    }
}
