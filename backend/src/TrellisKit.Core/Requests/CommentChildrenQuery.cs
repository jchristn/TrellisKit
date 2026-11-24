namespace TrellisKit.Core.Requests
{
    /// <summary>
    /// Enumeration query of the comment children route; accepts cursor as an alias of continuationToken (moreChildren carries both).
    /// </summary>
    public class CommentChildrenQuery : EnumerationQuery
    {
        #region Public-Members

        /// <summary>
        /// Alias of continuationToken.
        /// </summary>
        public string? Cursor { get; set; } = null;

        #endregion
    }
}
