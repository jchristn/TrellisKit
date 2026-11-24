namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Every stored profile value of one member, keyed by field id.
    /// </summary>
    public class ProfileValueSet
    {
        #region Public-Members

        /// <summary>
        /// Member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Scalar, select, and rank values by field id.
        /// </summary>
        public Dictionary<string, ProfileValue> Values
        {
            get { return _Values; }
            set { _Values = value ?? new Dictionary<string, ProfileValue>(StringComparer.Ordinal); }
        }

        /// <summary>
        /// History entries by field id, in position order.
        /// </summary>
        public Dictionary<string, List<ProfileHistoryEntry>> History
        {
            get { return _History; }
            set { _History = value ?? new Dictionary<string, List<ProfileHistoryEntry>>(StringComparer.Ordinal); }
        }

        #endregion

        #region Private-Members

        private Dictionary<string, ProfileValue> _Values = new Dictionary<string, ProfileValue>(StringComparer.Ordinal);
        private Dictionary<string, List<ProfileHistoryEntry>> _History = new Dictionary<string, List<ProfileHistoryEntry>>(StringComparer.Ordinal);

        #endregion
    }
}
