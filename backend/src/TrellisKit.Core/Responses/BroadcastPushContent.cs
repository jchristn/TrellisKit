namespace TrellisKit.Core.Responses
{
    /// <summary>
    /// The push part of a broadcast (docs/PHASE5_CONTRACT.md section 2.5), in requests and responses.
    /// </summary>
    public class BroadcastPushContent
    {
        #region Public-Members

        /// <summary>
        /// Push title (at most 100 characters), or null for the announcement title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Push body (at most 240 characters), or null.
        /// </summary>
        public string? Body { get; set; } = null;

        #endregion
    }
}
