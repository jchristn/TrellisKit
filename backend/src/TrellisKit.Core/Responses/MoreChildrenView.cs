namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Replies of a comment not included in its children (contract section 7.3). cursor and continuationToken carry the same token for POST /api/v1/comments/{id}/children/enumerate (null: start from the first reply).
    /// </summary>
    public class MoreChildrenView
    {
        #region Public-Members

        /// <summary>
        /// Replies not shown at that level.
        /// </summary>
        public int RemainingCount { get; set; } = 0;

        /// <summary>
        /// Continuation token for the children route, or null.
        /// </summary>
        public string? Cursor { get; set; } = null;

        /// <summary>
        /// Same value as cursor.
        /// </summary>
        public string? ContinuationToken { get; set; } = null;

        /// <summary>
        /// Children enumeration path.
        /// </summary>
        public string Enumerate { get; set; } = String.Empty;

        #endregion
    }
}
