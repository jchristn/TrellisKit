namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// What the caller may do in a forum (docs/PHASE3_CONTRACT.md section 1.4).
    /// </summary>
    public class ForumViewer
    {
        #region Public-Members

        /// <summary>
        /// Whether the caller may create a thread.
        /// </summary>
        public bool CanCreateThread { get; set; } = false;

        /// <summary>
        /// Whether the caller may follow the forum.
        /// </summary>
        public bool CanFollow { get; set; } = false;

        /// <summary>
        /// Whether the caller moderates the forum.
        /// </summary>
        public bool CanModerate { get; set; } = false;

        /// <summary>
        /// The forum's allowed types filtered by the caller's rights.
        /// </summary>
        public List<ThreadTypeEnum> AllowedThreadTypes
        {
            get { return _AllowedThreadTypes; }
            set { _AllowedThreadTypes = value ?? new List<ThreadTypeEnum>(); }
        }

        #endregion

        #region Private-Members

        private List<ThreadTypeEnum> _AllowedThreadTypes = new List<ThreadTypeEnum>();

        #endregion
    }
}
