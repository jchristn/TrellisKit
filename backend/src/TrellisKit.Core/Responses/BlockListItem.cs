namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One entry of GET /api/v1/me/blocks.
    /// </summary>
    public class BlockListItem
    {
        #region Public-Members

        /// <summary>
        /// The blocked member.
        /// </summary>
        public UserSummary User { get; set; } = new UserSummary();

        /// <summary>
        /// When the block was created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels of the blocked member (their public User metadata; blocks have none of their own) (never null).
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags of the blocked member (their public User metadata; blocks have none of their own) (never null).
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
