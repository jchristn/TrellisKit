namespace TrellisKit.Core.Responses
{
    /// <summary>
    /// The in-app part of a broadcast (docs/PHASE5_CONTRACT.md section 2.5), in requests and responses.
    /// </summary>
    public class BroadcastInAppContent
    {
        #region Public-Members

        /// <summary>
        /// Announcement title (at most 200 characters), or null for the broadcast title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Announcement body (Markdown subset, at most 20000 characters).
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// The announcement path, /announcements/{id} (responses only).
        /// </summary>
        public string? Path { get; set; } = null;

        #endregion
    }
}
