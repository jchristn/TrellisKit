namespace TrellisKit.Server.Services.Ai.Benchmark
{
    using System;

    /// <summary>
    /// One model reply of the scope benchmark, checked by the deterministic post-call layer
    /// (<see cref="ScopeOutputCheck"/>).
    /// </summary>
    public class ScopeBenchmarkReply
    {
        #region Public-Members

        /// <summary>
        /// Stable identifier, unique in the set (for example reply-code-001).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Category (CodeReply, LeakReply, CleanReply).
        /// </summary>
        public string Category { get; set; } = String.Empty;

        /// <summary>
        /// What makes the reply hard (for example unfenced-sql, semicolons).
        /// </summary>
        public string Type { get; set; } = String.Empty;

        /// <summary>
        /// tune or holdout.
        /// </summary>
        public string Split { get; set; } = ScopeBenchmark.Tune;

        /// <summary>
        /// Reply text.
        /// </summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>
        /// Expected post-call decision. Never null.
        /// </summary>
        public ScopeReplyExpectation Expect
        {
            get { return _Expect; }
            set { _Expect = value ?? new ScopeReplyExpectation(); }
        }

        #endregion

        #region Private-Members

        private ScopeReplyExpectation _Expect = new ScopeReplyExpectation();

        #endregion
    }
}
