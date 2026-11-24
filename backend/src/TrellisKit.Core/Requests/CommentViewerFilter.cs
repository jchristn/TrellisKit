namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Which comments a viewer is shown in a tree page: visible comments, the viewer's own Hidden or Removed comments, every Hidden or Removed comment for moderators, and placeholders (deleted, removed, hidden, or by a blocked author) only when they have visible descendants.
    /// </summary>
    public class CommentViewerFilter
    {
        #region Public-Members

        /// <summary>
        /// Viewer, or null.
        /// </summary>
        public string? ViewerUserId { get; set; } = null;

        /// <summary>
        /// Whether the viewer moderates the feed.
        /// </summary>
        public bool IsModerator { get; set; } = false;

        /// <summary>
        /// Authors the viewer blocked (minus staff); their comments become BlockedAuthor placeholders (never null).
        /// </summary>
        public List<string> HiddenAuthorIds
        {
            get { return _HiddenAuthorIds; }
            set { _HiddenAuthorIds = value ?? new List<string>(); }
        }

        #endregion

        #region Private-Members

        private List<string> _HiddenAuthorIds = new List<string>();

        #endregion
    }
}
