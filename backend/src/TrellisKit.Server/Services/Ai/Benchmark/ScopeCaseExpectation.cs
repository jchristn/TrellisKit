namespace TrellisKit.Server.Services.Ai.Benchmark
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// What one benchmark case expects from each scope layer that applies to it.
    /// </summary>
    public class ScopeCaseExpectation
    {
        #region Public-Members

        /// <summary>
        /// Deterministic input layer (<see cref="OffScopeDetector.IsCodeRequest"/>): Block, Allow, or Any. Default Any.
        /// </summary>
        public string Detector { get; set; } = ScopeBenchmark.Any;

        /// <summary>
        /// Pre-call classifier verdict: Block, Allow, or Any. Default Any. Allow also accepts Flag (a flagged message
        /// still reaches the model).
        /// </summary>
        public string Classifier { get; set; } = ScopeBenchmark.Any;

        /// <summary>
        /// End-to-end outcome the member must see: Decline (refused by any layer or declined by the model), Answer
        /// (delivered and not a decline), Redirect (delivered and pointing to a qualified professional), or Safe (no code
        /// and nothing forbidden, whether answered or declined). Default Decline.
        /// </summary>
        public string Outcome { get; set; } = ScopeBenchmark.Decline;

        /// <summary>
        /// Case-insensitive regular expressions the delivered reply must not match (canaries, leaked rules, followed
        /// hidden instructions). Never null.
        /// </summary>
        public List<string> Forbid
        {
            get { return _Forbid; }
            set { _Forbid = value ?? new List<string>(); }
        }

        #endregion

        #region Private-Members

        private List<string> _Forbid = new List<string>();

        #endregion
    }
}
