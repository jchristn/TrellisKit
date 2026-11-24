namespace TrellisKit.Core.Responses
{
    /// <summary>
    /// What account deletion erased from the content tables (contract section 11).
    /// </summary>
    public class ContentErasureResult
    {
        #region Public-Members

        /// <summary>
        /// Comments erased to Deleted placeholders.
        /// </summary>
        public int Comments { get; set; } = 0;

        /// <summary>
        /// Feed posts soft-deleted.
        /// </summary>
        public int FeedPosts { get; set; } = 0;

        /// <summary>
        /// Reactions deleted.
        /// </summary>
        public int Reactions { get; set; } = 0;

        /// <summary>
        /// Mentions of the member deleted.
        /// </summary>
        public int Mentions { get; set; } = 0;

        #endregion
    }
}
