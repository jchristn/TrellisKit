namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One row of a who-reacted enumeration (contract section 6.3).
    /// </summary>
    public class ReactionListItem
    {
        #region Public-Members

        /// <summary>
        /// Reaction identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Who reacted.
        /// </summary>
        public UserSummary User { get; set; } = new UserSummary();

        /// <summary>
        /// Reaction key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// When (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels of the reaction (never null).
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags of the reaction (never null).
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
