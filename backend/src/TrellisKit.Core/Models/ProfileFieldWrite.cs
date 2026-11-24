namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One field of a member's profile write: the field's rows are replaced (or cleared when both values are null).
    /// </summary>
    public class ProfileFieldWrite
    {
        #region Public-Members

        /// <summary>
        /// Field.
        /// </summary>
        public string FieldId { get; set; } = String.Empty;

        /// <summary>
        /// New value (non-History types), or null.
        /// </summary>
        public ProfileValue? Value { get; set; } = null;

        /// <summary>
        /// New entries (History type), or null.
        /// </summary>
        public List<ProfileHistoryEntry>? History { get; set; } = null;

        /// <summary>
        /// Lowercased words for member search (searchable fields).
        /// </summary>
        public List<string> SearchWords
        {
            get { return _SearchWords; }
            set { _SearchWords = value ?? new List<string>(); }
        }

        #endregion

        #region Private-Members

        private List<string> _SearchWords = new List<string>();

        #endregion
    }
}
