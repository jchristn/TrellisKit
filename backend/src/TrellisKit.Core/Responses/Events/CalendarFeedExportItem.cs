namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The calendar feed in the account export (created date only; never the secret).
    /// </summary>
    public class CalendarFeedExportItem
    {
        #region Public-Members

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last rotation, or null.
        /// </summary>
        public DateTime? RotatedUtc { get; set; } = null;

        /// <summary>
        /// Whether the feed serves.
        /// </summary>
        public bool IsEnabled { get; set; } = false;

        #endregion
    }
}
