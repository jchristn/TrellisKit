namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A count keyed by a day or a ticket type key (support metrics).
    /// </summary>
    public class TicketCount
    {
        #region Public-Members

        /// <summary>
        /// Key: a day as yyyy-MM-dd, or a ticket type key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Count.
        /// </summary>
        public long Count { get; set; } = 0;

        #endregion
    }
}
