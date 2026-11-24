namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// A count for one UTC day.
    /// </summary>
    public class DailyCount
    {
        #region Public-Members

        /// <summary>
        /// Day as YYYY-MM-DD.
        /// </summary>
        public string Date { get; set; } = String.Empty;

        /// <summary>
        /// Count.
        /// </summary>
        public long Count { get; set; } = 0;

        #endregion
    }
}
