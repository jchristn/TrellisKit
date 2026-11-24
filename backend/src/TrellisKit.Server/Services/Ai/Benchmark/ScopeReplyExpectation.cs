namespace TrellisKit.Server.Services.Ai.Benchmark
{
    using System;

    /// <summary>
    /// What the post-call check must do with one reply probe.
    /// </summary>
    public class ScopeReplyExpectation
    {
        #region Public-Members

        /// <summary>
        /// Pass (deliver) or Reject (retry, then refuse). Default Reject.
        /// </summary>
        public string OutputCheck { get; set; } = ScopeBenchmark.Reject;

        #endregion
    }
}
