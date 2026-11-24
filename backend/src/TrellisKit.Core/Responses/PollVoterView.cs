namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A poll voter (polls with voter visibility Visible).
    /// </summary>
    public class PollVoterView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Voter.
        /// </summary>
        public UserSummary User
        {
            get { return _User; }
            set { _User = value ?? new UserSummary(); }
        }

        /// <summary>
        /// Chosen options.
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

        /// <summary>
        /// The voter's labels.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// The voter's tags.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        #endregion

        #region Private-Members

        private UserSummary _User = new UserSummary();
        private List<string> _OptionIds = new List<string>();
        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
