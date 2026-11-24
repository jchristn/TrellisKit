namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// One event of the events map query with the address visibility the caller gets (docs/PHASE4_CONTRACT.md section 4.2).
    /// </summary>
    public class EventMapPoint
    {
        #region Public-Members

        /// <summary>
        /// The event.
        /// </summary>
        public EventRecord Event { get; set; } = new EventRecord();

        /// <summary>
        /// Effective visibility for the caller: Exact or Approximate (the stricter of the event's and its venue's).
        /// </summary>
        public string Visibility { get; set; } = "Exact";

        #endregion
    }
}
