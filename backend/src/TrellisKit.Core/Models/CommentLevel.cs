namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One level of a comment tree page: the replies returned under each parent (at most the per-parent limit plus one,
    /// in sibling order) and the number of replies of each parent that the viewer is shown at all (visible replies plus
    /// placeholders that keep the tree's shape).
    /// </summary>
    public class CommentLevel
    {
        #region Public-Members

        /// <summary>
        /// Replies, grouped by parent in sibling order (never null).
        /// </summary>
        public List<Comment> Comments
        {
            get { return _Comments; }
            set { _Comments = value ?? new List<Comment>(); }
        }

        /// <summary>
        /// Eligible replies per parent identifier (never null).
        /// </summary>
        public Dictionary<string, int> Totals
        {
            get { return _Totals; }
            set { _Totals = value ?? new Dictionary<string, int>(StringComparer.Ordinal); }
        }

        #endregion

        #region Private-Members

        private List<Comment> _Comments = new List<Comment>();
        private Dictionary<string, int> _Totals = new Dictionary<string, int>(StringComparer.Ordinal);

        #endregion
    }
}
