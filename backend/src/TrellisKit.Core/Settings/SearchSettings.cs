namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Search settings (resolved decisions 11 and 13: global search on the engine search index).
    /// </summary>
    public class SearchSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether search and the search indexer run. Default true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Database schema of the search index tables; empty uses the engine schema name plus _search (search when the engine uses the default schema).
        /// </summary>
        public string Schema
        {
            get { return _Schema; }
            set { _Schema = String.IsNullOrWhiteSpace(value) ? String.Empty : value.Trim(); }
        }

        /// <summary>
        /// Whether indexed and searched words are reduced to their base forms. Default true.
        /// </summary>
        public bool Lemmatize { get; set; } = true;

        /// <summary>
        /// Whether common words are ignored. Default true.
        /// </summary>
        public bool RemoveStopWords { get; set; } = true;

        /// <summary>
        /// How often the indexer drains its queue. Default 5. Minimum 1, maximum 3600.
        /// </summary>
        public int IndexIntervalSeconds
        {
            get { return _IndexIntervalSeconds; }
            set { _IndexIntervalSeconds = Math.Clamp(value, 1, 3600); }
        }

        /// <summary>
        /// Objects indexed per pass. Default 200. Minimum 1, maximum 5000.
        /// </summary>
        public int IndexBatchSize
        {
            get { return _IndexBatchSize; }
            set { _IndexBatchSize = Math.Clamp(value, 1, 5000); }
        }

        /// <summary>
        /// Ranked candidates read from the index per search before visibility filters. Default 500. Minimum 50, maximum 5000.
        /// </summary>
        public int CandidateLimit
        {
            get { return _CandidateLimit; }
            set { _CandidateLimit = Math.Clamp(value, 50, 5000); }
        }

        /// <summary>
        /// Global searches per member each minute. Default 60. Minimum 1, maximum 10000.
        /// </summary>
        public int PerMinute
        {
            get { return _PerMinute; }
            set { _PerMinute = Math.Clamp(value, 1, 10000); }
        }

        #endregion

        #region Private-Members

        private string _Schema = String.Empty;
        private int _IndexIntervalSeconds = 5;
        private int _IndexBatchSize = 200;
        private int _CandidateLimit = 500;
        private int _PerMinute = 60;

        #endregion
    }
}
