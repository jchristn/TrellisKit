namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Event details on a global search result. Occurrences of one recurring series collapse into a single result that
    /// opens the next upcoming occurrence (or the latest one when none is upcoming).
    /// </summary>
    public class SearchEventView
    {
        #region Public-Members

        /// <summary>
        /// Start of the occurrence the result opens (UTC).
        /// </summary>
        public DateTime StartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// IANA time zone of the event.
        /// </summary>
        public string TimeZone { get; set; } = "UTC";

        /// <summary>
        /// Whether the occurrence the result opens is canceled.
        /// </summary>
        public bool IsCanceled { get; set; } = false;

        /// <summary>
        /// Whether the occurrence has ended.
        /// </summary>
        public bool IsPast { get; set; } = false;

        /// <summary>
        /// Recurring series identifier, or null for a one-time event.
        /// </summary>
        public string? RecurrenceId { get; set; } = null;

        /// <summary>
        /// Plain-language rule of the series (for example "Every week on Tuesday"), or null.
        /// </summary>
        public string? RecurrenceSummary { get; set; } = null;

        /// <summary>
        /// Matching occurrences of the series collapsed into this result (1 for a one-time event).
        /// </summary>
        public int OccurrenceCount { get; set; } = 1;

        #endregion
    }
}
