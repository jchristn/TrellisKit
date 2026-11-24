namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A global search result (Phase 3 client assumption 1).
    /// </summary>
    public class SearchResultItem : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// FeedPost, Thread, Comment, or User.
        /// </summary>
        public string Type { get; set; } = String.Empty;

        /// <summary>
        /// Object identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Title, or null.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Plain-text snippet with highlights.
        /// </summary>
        public SearchSnippetView Snippet
        {
            get { return _Snippet; }
            set { _Snippet = value ?? new SearchSnippetView(); }
        }

        /// <summary>
        /// App-relative path.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// Author, or null.
        /// </summary>
        public UserSummary? Author { get; set; } = null;

        /// <summary>
        /// Forum of a thread (or of a thread comment), or null.
        /// </summary>
        public ForumRefView? Forum { get; set; } = null;

        /// <summary>
        /// Creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Event details of an Event result (start, canceled, and the recurring series it collapses), or null for other
        /// types.
        /// </summary>
        public SearchEventView? Event { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        #endregion

        #region Private-Members

        private SearchSnippetView _Snippet = new SearchSnippetView();
        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
