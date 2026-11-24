namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Enumeration (paging) limits shared by every list route (docs/ENUMERATION_AND_METADATA.md section 2).
    /// </summary>
    public class EnumerationSettings
    {
        #region Public-Members

        /// <summary>
        /// Hard cap on maxResults for any route, regardless of settings.
        /// </summary>
        public const int HardMaximum = 1000;

        /// <summary>
        /// Largest page a member route returns; larger maxResults values are clamped. Default 100. Minimum 1, maximum 1000.
        /// </summary>
        public int MaxResultsMember
        {
            get { return _MaxResultsMember; }
            set { _MaxResultsMember = Math.Clamp(value, 1, HardMaximum); }
        }

        /// <summary>
        /// Largest page an admin route returns; larger maxResults values are clamped. Default 500. Minimum 1, maximum 1000.
        /// </summary>
        public int MaxResultsAdmin
        {
            get { return _MaxResultsAdmin; }
            set { _MaxResultsAdmin = Math.Clamp(value, 1, HardMaximum); }
        }

        /// <summary>
        /// Whether member routes return totalRecords and recordsRemaining (admin routes always do). Default true.
        /// </summary>
        public bool CountMemberTotals { get; set; } = true;

        #endregion

        #region Private-Members

        private int _MaxResultsMember = 100;
        private int _MaxResultsAdmin = 500;

        #endregion
    }
}
