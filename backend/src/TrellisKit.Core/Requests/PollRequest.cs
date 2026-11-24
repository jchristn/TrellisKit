namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// A poll in a thread request (docs/PHASE3_CONTRACT.md section 6.1).
    /// </summary>
    public class PollRequest
    {
        #region Public-Members

        /// <summary>
        /// 2 to polls.maxOptions options.
        /// </summary>
        public List<PollOptionRequest>? Options { get; set; } = null;

        /// <summary>
        /// Whether several options may be chosen (default false).
        /// </summary>
        public bool? MultipleChoice { get; set; } = null;

        /// <summary>
        /// Multiple choice limit (2 to the option count), or null for any number.
        /// </summary>
        public int? MaxChoices { get; set; } = null;

        /// <summary>
        /// Scheduled close (5 minutes to polls.maxDurationDays ahead), or null.
        /// </summary>
        public DateTime? ClosesUtc { get; set; } = null;

        /// <summary>
        /// When results are shown (default AfterVote).
        /// </summary>
        public PollResultsVisibilityEnum? ResultsVisibility { get; set; } = null;

        /// <summary>
        /// Whether voters are listed (default Anonymous).
        /// </summary>
        public PollVoterVisibilityEnum? VoterVisibility { get; set; } = null;

        /// <summary>
        /// Whether votes may change (default true).
        /// </summary>
        public bool? AllowVoteChange { get; set; } = null;

        #endregion
    }
}
