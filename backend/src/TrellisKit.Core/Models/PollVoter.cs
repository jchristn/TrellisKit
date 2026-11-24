namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// One voter of a poll with their choices (voter visibility Visible).
    /// </summary>
    public class PollVoter
    {
        #region Public-Members

        /// <summary>
        /// Voter.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Chosen option identifiers.
        /// </summary>
        public List<string> OptionIds
        {
            get { return _OptionIds; }
            set { _OptionIds = value ?? new List<string>(); }
        }

        /// <summary>
        /// Latest vote time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private List<string> _OptionIds = new List<string>();

        #endregion
    }
}
