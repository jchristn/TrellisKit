namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A member's personal calendar feed (docs/PHASE4_CONTRACT.md section 4.5).
    /// </summary>
    public class CalendarFeedView
    {
        #region Public-Members

        /// <summary>
        /// Subscription URL (contains the secret; shown only to the member).
        /// </summary>
        public string Url { get; set; } = String.Empty;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last rotation, or null.
        /// </summary>
        public DateTime? RotatedUtc { get; set; } = null;

        /// <summary>
        /// Last fetch, or null.
        /// </summary>
        public DateTime? LastFetchedUtc { get; set; } = null;

        #endregion
    }
}
