namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Bounded preview of recent health records.
    /// </summary>
    public class HealthRecentView
    {
        #region Public-Members

        /// <summary>
        /// Up to 20 newest records.
        /// </summary>
        public List<HealthCheckRecordView> Objects { get; set; } = new List<HealthCheckRecordView>();

        /// <summary>
        /// Records kept.
        /// </summary>
        public long TotalRecords { get; set; } = 0;

        /// <summary>
        /// Route of the full enumeration.
        /// </summary>
        public string Enumerate { get; set; } = String.Empty;

        #endregion
    }
}
