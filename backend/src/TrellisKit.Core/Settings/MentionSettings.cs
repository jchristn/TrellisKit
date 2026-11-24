namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Mention settings (contract section 3.3).
    /// </summary>
    public class MentionSettings
    {
        #region Public-Members

        /// <summary>
        /// Distinct members stored and notified per post or comment; the rest render as plain text. Default 10. Minimum 0, maximum 50.
        /// </summary>
        public int MaxNotifiedPerItem
        {
            get { return _MaxNotifiedPerItem; }
            set { _MaxNotifiedPerItem = Math.Clamp(value, 0, 50); }
        }

        #endregion

        #region Private-Members

        private int _MaxNotifiedPerItem = 10;

        #endregion
    }
}
