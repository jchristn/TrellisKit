namespace TrellisKit.Core.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// Reaction fields of a reactable payload, also the body of PUT and DELETE .../reactions/{key} (contract sections 2 and 6.2).
    /// </summary>
    public class ReactionState
    {
        #region Public-Members

        /// <summary>
        /// Counts of keys above zero that are in the property's current set.
        /// </summary>
        public Dictionary<string, long> ReactionCounts { get; set; } = new Dictionary<string, long>();

        /// <summary>
        /// Sum of reactionCounts.
        /// </summary>
        public long ReactionTotal { get; set; } = 0;

        /// <summary>
        /// The caller's keys (current set only) (never null).
        /// </summary>
        public List<string> MyReactions
        {
            get { return _MyReactions; }
            set { _MyReactions = value ?? new List<string>(); }
        }

        #endregion

        #region Private-Members

        private List<string> _MyReactions = new List<string>();

        #endregion
    }
}
