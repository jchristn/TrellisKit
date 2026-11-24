namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A document in the search index: one engine object (identifier "Type:id").
    /// </summary>
    public class SearchDocument
    {
        #region Public-Members

        /// <summary>
        /// Document identifier ("Thread:thr_...", "Comment:cmt_...", "FeedPost:fpo_...", "User:usr_...").
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Indexed text (titles repeated to weigh them higher).
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Normalized labels of the object.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Normalized tags of the object plus engine tags (tk.type, tk.forum, tk.thread).
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(StringComparer.Ordinal); }
        }

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion
    }
}
