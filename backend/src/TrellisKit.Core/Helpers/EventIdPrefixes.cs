namespace TrellisKit.Core.Helpers
{
    /// <summary>
    /// Identifier prefixes of the Phase 4 event objects (docs/PHASE4_CONTRACT.md section 0.1; recurrence from resolved
    /// decision 11).
    /// </summary>
    public static class EventIdPrefixes
    {
        #region Public-Members

        /// <summary>
        /// Event (and occurrence) identifier prefix.
        /// </summary>
        public const string Event = "evt_";

        /// <summary>
        /// Event category identifier prefix.
        /// </summary>
        public const string Category = "ecg_";

        /// <summary>
        /// RSVP identifier prefix.
        /// </summary>
        public const string Rsvp = "rsv_";

        /// <summary>
        /// Reminder identifier prefix.
        /// </summary>
        public const string Reminder = "erm_";

        /// <summary>
        /// Calendar feed identifier prefix.
        /// </summary>
        public const string CalendarFeed = "cfd_";

        /// <summary>
        /// Recurrence identifier prefix.
        /// </summary>
        public const string Recurrence = "erc_";

        /// <summary>
        /// Recurrence exception identifier prefix.
        /// </summary>
        public const string RecurrenceException = "erx_";

        #endregion
    }
}
