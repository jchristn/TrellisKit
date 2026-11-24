namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// A poll row (docs/PHASE3_CONTRACT.md section 6).
    /// </summary>
    public class Poll
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
        /// Whether a voter may choose several options.
        /// </summary>
        public bool MultipleChoice { get; set; } = false;

        /// <summary>
        /// Multiple choice limit, or null for any number.
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
        /// Whether voters may change or remove their vote.
        /// </summary>
        public bool AllowVoteChange { get; set; } = true;

        /// <summary>
        /// Scheduled close, or null.
        /// </summary>
        public DateTime? ClosesUtc { get; set; } = null;

        /// <summary>
        /// When the poll closed, or null.
        /// </summary>
        public DateTime? ClosedUtc { get; set; } = null;

        /// <summary>
        /// Who closed it early, or null.
        /// </summary>
        public string? ClosedByUserId { get; set; } = null;

        /// <summary>
        /// Distinct voters.
        /// </summary>
        public int TotalVoters { get; set; } = 0;

        /// <summary>
        /// First vote time, or null before the first vote.
        /// </summary>
        public DateTime? FirstVoteUtc { get; set; } = null;

        /// <summary>
        /// Creation time (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Options in display order (bounded by polls.maxOptions).
        /// </summary>
        public List<PollOption> Options
        {
            get { return _Options; }
            set { _Options = value ?? new List<PollOption>(); }
        }

        #endregion

        #region Private-Members

        private List<PollOption> _Options = new List<PollOption>();

        #endregion
    }
}
