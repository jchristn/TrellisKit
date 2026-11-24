namespace TrellisKit.Core.Responses
{
    /// <summary>
    /// A comment tree page (contract section 7.3 as aligned with docs/ENUMERATION_AND_METADATA.md): an
    /// EnumerationResult of top-level comments (or of the replies of one comment for the children route) with children
    /// nested up to the page's depth and childLimit, plus the post's visible comment count.
    /// </summary>
    public class CommentTreePage : EnumerationResult<CommentView>
    {
        #region Public-Members

        /// <summary>
        /// Visible comments on the post (not deleted, removed, or hidden).
        /// </summary>
        public long CommentCount { get; set; } = 0;

        /// <summary>
        /// The accepted comment of a Question thread, or null.
        /// </summary>
        public string? AcceptedAnswerId { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Wrap an enumeration page.
        /// </summary>
        /// <param name="page">Page.</param>
        /// <param name="commentCount">Visible comments on the post.</param>
        /// <returns>Tree page.</returns>
        public static CommentTreePage From(EnumerationResult<CommentView> page, long commentCount)
        {
            System.ArgumentNullException.ThrowIfNull(page);
            return new CommentTreePage
            {
                Success = page.Success,
                MaxResults = page.MaxResults,
                Skip = page.Skip,
                ContinuationToken = page.ContinuationToken,
                EndOfResults = page.EndOfResults,
                TotalRecords = page.TotalRecords,
                RecordsRemaining = page.RecordsRemaining,
                TimestampUtc = page.TimestampUtc,
                Objects = page.Objects,
                CommentCount = commentCount
            };
        }

        #endregion
    }
}
