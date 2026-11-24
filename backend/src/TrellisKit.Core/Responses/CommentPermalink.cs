namespace TrellisKit.Core.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/comments/{id} (contract section 7.4). ancestors is bounded by comments.maxDepth.
    /// </summary>
    public class CommentPermalink
    {
        #region Public-Members

        /// <summary>
        /// The commented target.
        /// </summary>
        public ContentTarget Target { get; set; } = new ContentTarget();

        /// <summary>
        /// Ancestors without children, root first (never null).
        /// </summary>
        public List<CommentView> Ancestors
        {
            get { return _Ancestors; }
            set { _Ancestors = value ?? new List<CommentView>(); }
        }

        /// <summary>
        /// The comment with children.
        /// </summary>
        public CommentView Comment { get; set; } = new CommentView();

        #endregion

        #region Private-Members

        private List<CommentView> _Ancestors = new List<CommentView>();

        #endregion
    }
}
