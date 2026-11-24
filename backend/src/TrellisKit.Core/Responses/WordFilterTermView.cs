namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A word filter term as the admin console shows it.
    /// </summary>
    public class WordFilterTermView
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Term as entered.
        /// </summary>
        public string Term { get; set; } = String.Empty;

        /// <summary>
        /// Word or Substring.
        /// </summary>
        public WordFilterMatchTypeEnum MatchType { get; set; } = WordFilterMatchTypeEnum.Word;

        /// <summary>
        /// Block or Flag.
        /// </summary>
        public WordFilterActionEnum Action { get; set; } = WordFilterActionEnum.Block;

        /// <summary>
        /// Note, or null.
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Creator's display name ("admin token" when created with the admin token, "Deleted member" after the account
        /// was deleted).
        /// </summary>
        public string? CreatedByName { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last change (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Matches recorded in the last 30 days.
        /// </summary>
        public long HitCount30d { get; set; } = 0;

        /// <summary>
        /// Labels of the term (never null).
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags of the term (never null).
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
