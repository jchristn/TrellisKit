namespace TrellisKit.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Collection filters of the usage record enumeration.
    /// </summary>
    public class UsageRecordFilter
    {
        #region Public-Members

        /// <summary>
        /// Meter ids to include; empty includes every meter. Never null.
        /// </summary>
        public List<string> Meters
        {
            get { return _Meters; }
            set { _Meters = value ?? new List<string>(); }
        }

        /// <summary>
        /// First local day (yyyy-MM-dd, inclusive), or null.
        /// </summary>
        public string? FromDay { get; set; } = null;

        /// <summary>
        /// Last local day (yyyy-MM-dd, inclusive), or null.
        /// </summary>
        public string? ToDay { get; set; } = null;

        /// <summary>
        /// Closed state, or null for both.
        /// </summary>
        public bool? Closed { get; set; } = null;

        #endregion

        #region Private-Members

        private List<string> _Meters = new List<string>();

        #endregion
    }
}
