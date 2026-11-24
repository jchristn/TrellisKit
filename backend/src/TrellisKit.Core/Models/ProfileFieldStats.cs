namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Fill statistics of a profile field.
    /// </summary>
    public class ProfileFieldStats
    {
        #region Public-Members

        /// <summary>
        /// Members with a value.
        /// </summary>
        public long ValueCount { get; set; } = 0;

        /// <summary>
        /// Share of non-deleted members with a value (0 to 1).
        /// </summary>
        public double FillRate { get; set; } = 0;

        /// <summary>
        /// Members per option (select and rank types; bounded by the options).
        /// </summary>
        public List<ProfileOptionCount> OptionCounts
        {
            get { return _OptionCounts; }
            set { _OptionCounts = value ?? new List<ProfileOptionCount>(); }
        }

        /// <summary>
        /// Values written in the last 30 days.
        /// </summary>
        public long Updated30d { get; set; } = 0;

        #endregion

        #region Private-Members

        private List<ProfileOptionCount> _OptionCounts = new List<ProfileOptionCount>();

        #endregion
    }
}
