namespace TrellisKit.Server.Services.Ai.Benchmark
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The versioned off-scope and prompt-injection benchmark (docs/INTEGRATION.md owner requirement R7), loaded from
    /// Benchmarks/scope-benchmark.json embedded in the server. See docs/BENCHMARKS.md.
    /// </summary>
    public class ScopeBenchmarkSet
    {
        #region Public-Members

        /// <summary>
        /// Dataset name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Dataset version; increases with every change to cases, replies, or thresholds. Minimum 1.
        /// </summary>
        public int Version { get; set; } = 1;

        /// <summary>
        /// Date the set was frozen (yyyy-MM-dd).
        /// </summary>
        public string FrozenUtc { get; set; } = String.Empty;

        /// <summary>
        /// What the set measures.
        /// </summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Thresholds. Never null.
        /// </summary>
        public ScopeBenchmarkThresholds Thresholds
        {
            get { return _Thresholds; }
            set { _Thresholds = value ?? new ScopeBenchmarkThresholds(); }
        }

        /// <summary>
        /// Category descriptions by name. Never null.
        /// </summary>
        public Dictionary<string, string> Categories
        {
            get { return _Categories; }
            set { _Categories = value ?? new Dictionary<string, string>(); }
        }

        /// <summary>
        /// Member messages. Never null.
        /// </summary>
        public List<ScopeBenchmarkCase> Cases
        {
            get { return _Cases; }
            set { _Cases = value ?? new List<ScopeBenchmarkCase>(); }
        }

        /// <summary>
        /// Reply probes for the post-call check. Never null.
        /// </summary>
        public List<ScopeBenchmarkReply> Replies
        {
            get { return _Replies; }
            set { _Replies = value ?? new List<ScopeBenchmarkReply>(); }
        }

        /// <summary>
        /// SHA-256 (lowercase hex) of the dataset file, recorded in every report.
        /// </summary>
        [JsonIgnore]
        public string Sha256 { get; set; } = String.Empty;

        /// <summary>
        /// The property overlay merged into the set ("name vN"), or null for the engine set alone.
        /// </summary>
        [JsonIgnore]
        public string? Overlay { get; set; } = null;

        #endregion

        #region Private-Members

        private ScopeBenchmarkThresholds _Thresholds = new ScopeBenchmarkThresholds();
        private Dictionary<string, string> _Categories = new Dictionary<string, string>();
        private List<ScopeBenchmarkCase> _Cases = new List<ScopeBenchmarkCase>();
        private List<ScopeBenchmarkReply> _Replies = new List<ScopeBenchmarkReply>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// The case with an identifier, or null.
        /// </summary>
        /// <param name="id">Case identifier.</param>
        /// <returns>Case or null.</returns>
        public ScopeBenchmarkCase? Find(string? id)
        {
            if (String.IsNullOrWhiteSpace(id)) return null;
            foreach (ScopeBenchmarkCase c in _Cases) if (String.Equals(c.Id, id, StringComparison.Ordinal)) return c;
            return null;
        }

        #endregion
    }
}
