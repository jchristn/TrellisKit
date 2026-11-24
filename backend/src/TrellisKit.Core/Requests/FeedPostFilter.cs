namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Typed conditions of the member and public feed enumerations (published, not deleted, moderation None).
    /// </summary>
    public class FeedPostFilter
    {
        #region Public-Members

        /// <summary>
        /// Authors whose posts the viewer does not see (blocked by the viewer, minus staff) (never null).
        /// </summary>
        public List<string> HiddenAuthorIds
        {
            get { return _HiddenAuthorIds; }
            set { _HiddenAuthorIds = value ?? new List<string>(); }
        }

        /// <summary>
        /// Only unpinned posts (the Feed ordering's sequence after the pinned prefix).
        /// </summary>
        public bool UnpinnedOnly { get; set; } = false;

        /// <summary>
        /// Only Public posts (audience Public).
        /// </summary>
        public bool PublicOnly { get; set; } = false;

        /// <summary>
        /// Lower bound on publishedUtc (filters.publishedAfterUtc).
        /// </summary>
        public DateTime? PublishedAfterUtc { get; set; } = null;

        /// <summary>
        /// Upper bound on publishedUtc (filters.publishedBeforeUtc).
        /// </summary>
        public DateTime? PublishedBeforeUtc { get; set; } = null;

        /// <summary>
        /// Audiences the posts may have (the caller's visible audiences, narrowed by filters.audience), or null for any.
        /// An empty list matches nothing.
        /// </summary>
        public List<ContentAudienceEnum>? Audiences { get; set; } = null;

        #endregion

        #region Private-Members

        private List<string> _HiddenAuthorIds = new List<string>();

        #endregion
    }
}
