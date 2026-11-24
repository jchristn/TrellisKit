namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A poll (docs/PHASE3_CONTRACT.md section 6.2).
    /// </summary>
    public class PollView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (pol_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Thread identifier.
        /// </summary>
        public string ThreadId { get; set; } = String.Empty;

        /// <summary>
        /// Whether several options may be chosen.
        /// </summary>
        public bool MultipleChoice { get; set; } = false;

        /// <summary>
        /// Multiple choice limit, or null.
        /// </summary>
        public int? MaxChoices { get; set; } = null;

        /// <summary>
        /// When results are shown.
        /// </summary>
        public PollResultsVisibilityEnum ResultsVisibility { get; set; } = PollResultsVisibilityEnum.AfterVote;

        /// <summary>
        /// Whether voters are listed.
        /// </summary>
        public PollVoterVisibilityEnum VoterVisibility { get; set; } = PollVoterVisibilityEnum.Anonymous;

        /// <summary>
        /// Whether votes may change.
        /// </summary>
        public bool AllowVoteChange { get; set; } = true;

        /// <summary>
        /// Scheduled close, or null.
        /// </summary>
        public DateTime? ClosesUtc { get; set; } = null;

        /// <summary>
        /// Close time, or null.
        /// </summary>
        public DateTime? ClosedUtc { get; set; } = null;

        /// <summary>
        /// Whether the poll is closed.
        /// </summary>
        public bool IsClosed { get; set; } = false;

        /// <summary>
        /// Distinct voters.
        /// </summary>
        public int TotalVoters { get; set; } = 0;

        /// <summary>
        /// Whether the caller sees the counts.
        /// </summary>
        public bool ResultsVisible { get; set; } = false;

        /// <summary>
        /// The caller's options.
        /// </summary>
        public List<string> MyVote
        {
            get { return _MyVote; }
            set { _MyVote = value ?? new List<string>(); }
        }

        /// <summary>
        /// Options (bounded by polls.maxOptions).
        /// </summary>
        public List<PollOptionView> Options
        {
            get { return _Options; }
            set { _Options = value ?? new List<PollOptionView>(); }
        }

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        #endregion

        #region Private-Members

        private List<string> _MyVote = new List<string>();
        private List<PollOptionView> _Options = new List<PollOptionView>();
        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
