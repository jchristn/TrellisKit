namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// The result of a vote write.
    /// </summary>
    public class PollVoteOutcome
    {
        #region Public-Members

        /// <summary>
        /// Whether the vote set was stored.
        /// </summary>
        public bool Applied { get; set; } = false;

        /// <summary>
        /// Whether the poll was closed.
        /// </summary>
        public bool Closed { get; set; } = false;

        /// <summary>
        /// Whether a different earlier vote blocked the change (allowVoteChange false).
        /// </summary>
        public bool Locked { get; set; } = false;

        /// <summary>
        /// Whether the caller had voted before with a different set.
        /// </summary>
        public bool Changed { get; set; } = false;

        /// <summary>
        /// Whether the caller had voted before.
        /// </summary>
        public bool HadVote { get; set; } = false;

        /// <summary>
        /// The caller's options after the write.
        /// </summary>
        public List<string> Current
        {
            get { return _Current; }
            set { _Current = value ?? new List<string>(); }
        }

        #endregion

        #region Private-Members

        private List<string> _Current = new List<string>();

        #endregion
    }
}
