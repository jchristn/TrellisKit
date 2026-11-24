namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Ranked thread candidates of a thread search, before the list's visibility filters.
    /// </summary>
    public class ThreadSearchCandidates
    {
        #region Public-Members

        /// <summary>
        /// Thread identifiers by descending relevance (one per thread even when several comments match).
        /// </summary>
        public List<string> ThreadIds
        {
            get { return _ThreadIds; }
            set { _ThreadIds = value ?? new List<string>(); }
        }

        /// <summary>
        /// The best matching comment of each thread whose comments matched, keyed by thread identifier.
        /// </summary>
        public Dictionary<string, string> CommentHits
        {
            get { return _CommentHits; }
            set { _CommentHits = value ?? new Dictionary<string, string>(StringComparer.Ordinal); }
        }

        #endregion

        #region Private-Members

        private List<string> _ThreadIds = new List<string>();
        private Dictionary<string, string> _CommentHits = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion
    }
}
