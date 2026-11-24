namespace TrellisKit.Server.Services
{
    using System;

    /// <summary>
    /// A rendered calendar document.
    /// </summary>
    public class IcsDocument
    {
        #region Public-Members

        /// <summary>
        /// Download file name, for example autumn-meetup.ics.
        /// </summary>
        public string FileName { get; set; } = "event.ics";

        /// <summary>
        /// VCALENDAR text (CRLF line ends).
        /// </summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>
        /// Whether the document is a subscription feed (cached privately).
        /// </summary>
        public bool IsFeed { get; set; } = false;

        #endregion
    }
}
