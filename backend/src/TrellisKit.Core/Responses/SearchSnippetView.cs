namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A plain-text search snippet (docs/PHASE3_CONTRACT.md section 9.1).
    /// </summary>
    public class SearchSnippetView
    {
        #region Public-Members

        /// <summary>
        /// Title, Body, PollOption, or Comment.
        /// </summary>
        public string? Field { get; set; } = null;

        /// <summary>
        /// The matching comment, or null.
        /// </summary>
        public string? CommentId { get; set; } = null;

        /// <summary>
        /// Plain text (never HTML), at most 200 characters around the first match.
        /// </summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>
        /// Highlighted ranges.
        /// </summary>
        public List<SnippetHighlight> Highlights
        {
            get { return _Highlights; }
            set { _Highlights = value ?? new List<SnippetHighlight>(); }
        }

        #endregion

        #region Private-Members

        private List<SnippetHighlight> _Highlights = new List<SnippetHighlight>();

        #endregion
    }
}
