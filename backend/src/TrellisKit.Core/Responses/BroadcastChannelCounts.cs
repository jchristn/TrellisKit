namespace TrellisKit.Core.Responses
{
    /// <summary>
    /// Recipients each channel can reach in a segment preview.
    /// </summary>
    public class BroadcastChannelCounts
    {
        #region Public-Members

        /// <summary>
        /// In-app (every recipient).
        /// </summary>
        public long InApp { get; set; } = 0;

        /// <summary>
        /// Push (an active push token and the Announcements push preference on, unless Essential).
        /// </summary>
        public long Push { get; set; } = 0;

        /// <summary>
        /// Email (a verified address, the Announcements email preference on unless Essential, not suppressed).
        /// </summary>
        public long Email { get; set; } = 0;

        #endregion
    }
}
