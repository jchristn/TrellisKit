namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A poll option (docs/PHASE3_CONTRACT.md section 6.2).
    /// </summary>
    public class PollOptionView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (pop_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Display position.
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Votes, or null while results are hidden from the caller.
        /// </summary>
        public int? VoteCount { get; set; } = null;

        /// <summary>
        /// Percent of voters (one decimal), or null while results are hidden.
        /// </summary>
        public double? Percent { get; set; } = null;

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

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
