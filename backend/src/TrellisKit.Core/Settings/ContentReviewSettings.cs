namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Content changelog and Terms of Service review settings (docs/INTEGRATION.md content review contract). Not thread safe.
    /// </summary>
    public class ContentReviewSettings
    {
        #region Public-Members

        /// <summary>
        /// Pending rows one review run plans (oldest first). Default 200, minimum 1, maximum 5000.
        /// </summary>
        public int BatchSize
        {
            get { return _BatchSize; }
            set { _BatchSize = Math.Clamp(value, 1, 5000); }
        }

        /// <summary>
        /// Most characters of a snapshot sent to the reviewer; longer text is cut. Default 6000, minimum 200, maximum
        /// 20000.
        /// </summary>
        public int MaxReviewCharacters
        {
            get { return _MaxReviewCharacters; }
            set { _MaxReviewCharacters = Math.Clamp(value, 200, 20000); }
        }

        /// <summary>
        /// Output token cap of one review. Default 300, minimum 50, maximum 4000.
        /// </summary>
        public int MaxOutputTokens
        {
            get { return _MaxOutputTokens; }
            set { _MaxOutputTokens = Math.Clamp(value, 50, 4000); }
        }

        /// <summary>
        /// Failed reviews of one row (errors or unreadable verdicts) before it is flagged for an administrator instead,
        /// so nothing escapes review. Default 5, minimum 1, maximum 50.
        /// </summary>
        public int MaxAttempts
        {
            get { return _MaxAttempts; }
            set { _MaxAttempts = Math.Clamp(value, 1, 50); }
        }

        /// <summary>
        /// Days reviewed rows (Clean, Dismissed, Actioned) are kept; Pending and Flagged rows are kept until resolved.
        /// Default 400, minimum 30, maximum 3650.
        /// </summary>
        public int RetentionDays
        {
            get { return _RetentionDays; }
            set { _RetentionDays = Math.Clamp(value, 30, 3650); }
        }

        /// <summary>
        /// Most administrators notified about one flagged row. Default 25, minimum 1, maximum 100.
        /// </summary>
        public int MaxNotifiedAdmins
        {
            get { return _MaxNotifiedAdmins; }
            set { _MaxNotifiedAdmins = Math.Clamp(value, 1, 100); }
        }

        #endregion

        #region Private-Members

        private int _BatchSize = 200;
        private int _MaxReviewCharacters = 6000;
        private int _MaxOutputTokens = 300;
        private int _MaxAttempts = 5;
        private int _RetentionDays = 400;
        private int _MaxNotifiedAdmins = 25;

        #endregion
    }
}
