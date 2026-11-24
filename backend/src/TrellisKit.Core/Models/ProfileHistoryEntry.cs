namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One entry of a History profile field (profilehistoryentries plus profilehistoryvalues).
    /// </summary>
    public class ProfileHistoryEntry
    {
        #region Public-Members

        /// <summary>
        /// Identifier (phe_, or lin_ for entries migrated from rank history).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Field.
        /// </summary>
        public string FieldId { get; set; } = String.Empty;

        /// <summary>
        /// Display position.
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Sub-field values.
        /// </summary>
        public List<ProfileHistoryValue> Values
        {
            get { return _Values; }
            set { _Values = value ?? new List<ProfileHistoryValue>(); }
        }

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private List<ProfileHistoryValue> _Values = new List<ProfileHistoryValue>();

        #endregion
    }
}
