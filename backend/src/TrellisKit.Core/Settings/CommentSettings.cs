namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Comment settings (contract section 7).
    /// </summary>
    public class CommentSettings
    {
        #region Public-Members

        /// <summary>
        /// Longest comment body, in Unicode scalar values. Default 5000. Minimum 100, maximum 20000.
        /// </summary>
        public int MaxBodyLength
        {
            get { return _MaxBodyLength; }
            set { _MaxBodyLength = Math.Clamp(value, 100, 20000); }
        }

        /// <summary>
        /// Replies below this depth are stored as siblings with replyToCommentId. Default 8. Minimum 1, maximum 20.
        /// </summary>
        public int MaxDepth
        {
            get { return _MaxDepth; }
            set { _MaxDepth = Math.Clamp(value, 1, 20); }
        }

        /// <summary>
        /// Media items a comment may carry. 0 disables comment media. Default 1. Minimum 0, maximum 4.
        /// </summary>
        public int MaxAttachments
        {
            get { return _MaxAttachments; }
            set { _MaxAttachments = Math.Clamp(value, 0, 4); }
        }

        /// <summary>
        /// Comments a member may post each minute. Default 10. Minimum 1, maximum 1000.
        /// </summary>
        public int PerMinute
        {
            get { return _PerMinute; }
            set { _PerMinute = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Comments a member may post each day. Default 300. Minimum 1, maximum 100000.
        /// </summary>
        public int PerDay
        {
            get { return _PerDay; }
            set { _PerDay = Math.Clamp(value, 1, 100000); }
        }

        /// <summary>
        /// Accounts younger than this get the new account comment limits. Default 24. Minimum 0, maximum 720.
        /// </summary>
        public int NewAccountHours
        {
            get { return _NewAccountHours; }
            set { _NewAccountHours = Math.Clamp(value, 0, 720); }
        }

        /// <summary>
        /// Comments a new account may post each minute. Default 3. Minimum 1, maximum 1000.
        /// </summary>
        public int NewAccountPerMinute
        {
            get { return _NewAccountPerMinute; }
            set { _NewAccountPerMinute = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Comments a new account may post each day. Default 30. Minimum 1, maximum 100000.
        /// </summary>
        public int NewAccountPerDay
        {
            get { return _NewAccountPerDay; }
            set { _NewAccountPerDay = Math.Clamp(value, 1, 100000); }
        }

        #endregion

        #region Private-Members

        private int _MaxBodyLength = 5000;
        private int _MaxDepth = 8;
        private int _MaxAttachments = 1;
        private int _PerMinute = 10;
        private int _PerDay = 300;
        private int _NewAccountHours = 24;
        private int _NewAccountPerMinute = 3;
        private int _NewAccountPerDay = 30;

        #endregion
    }
}
