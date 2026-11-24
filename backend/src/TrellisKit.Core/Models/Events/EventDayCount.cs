namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A count for one UTC day (metrics).
    /// </summary>
    public class EventDayCount
    {
        #region Public-Members

        /// <summary>
        /// Day (yyyy-MM-dd).
        /// </summary>
        public string Day { get; set; } = String.Empty;

        /// <summary>
        /// Count.
        /// </summary>
        public long Count { get; set; } = 0;

        #endregion
    }
}
