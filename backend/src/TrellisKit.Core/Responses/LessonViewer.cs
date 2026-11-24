namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Viewer capabilities of a lesson (docs/PHASE4_CONTRACT.md section 1.3).
    /// </summary>
    public class LessonViewer
    {
        #region Public-Members

        /// <summary>
        /// May play the lesson (media grant, progress).
        /// </summary>
        public bool CanPlay { get; set; } = false;

        /// <summary>
        /// May download it for offline viewing (mobile).
        /// </summary>
        public bool CanDownload { get; set; } = false;

        /// <summary>
        /// May comment.
        /// </summary>
        public bool CanComment { get; set; } = false;

        /// <summary>
        /// May react.
        /// </summary>
        public bool CanReact { get; set; } = false;

        /// <summary>
        /// May report it.
        /// </summary>
        public bool CanReport { get; set; } = false;

        /// <summary>
        /// May edit it (library staff).
        /// </summary>
        public bool CanEdit { get; set; } = false;

        /// <summary>
        /// May promote it to the feed.
        /// </summary>
        public bool CanPromote { get; set; } = false;

        /// <summary>
        /// May open it (false on locked stubs).
        /// </summary>
        public bool CanOpen { get; set; } = true;

        /// <summary>
        /// SignIn, Membership, or null.
        /// </summary>
        public string? Requires { get; set; } = null;

        #endregion
    }
}
