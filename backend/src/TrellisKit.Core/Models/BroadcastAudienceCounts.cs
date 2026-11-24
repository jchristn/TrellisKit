namespace TrellisKit.Core.Models
{
    /// <summary>
    /// Audience counts of a segment: every recipient (the in-app count), those a push can reach (an active push token and
    /// the Announcements push preference on, unless Essential), and those an email can reach (a verified address, the
    /// Announcements email preference on unless Essential, and not suppressed).
    /// </summary>
    public class BroadcastAudienceCounts
    {
        #region Public-Members

        /// <summary>
        /// Recipients.
        /// </summary>
        public long Count { get; set; } = 0;

        /// <summary>
        /// Recipients a push can reach.
        /// </summary>
        public long Push { get; set; } = 0;

        /// <summary>
        /// Recipients an email can reach.
        /// </summary>
        public long Email { get; set; } = 0;

        #endregion
    }
}
