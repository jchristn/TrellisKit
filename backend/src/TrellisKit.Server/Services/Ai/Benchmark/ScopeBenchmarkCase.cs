namespace TrellisKit.Server.Services.Ai.Benchmark
{
    using System;

    /// <summary>
    /// One member message of the scope benchmark (a probe with a known correct outcome).
    /// </summary>
    public class ScopeBenchmarkCase
    {
        #region Public-Members

        /// <summary>
        /// Stable identifier, unique in the set (for example code-001).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Category (CodeRequest, GeneralAssistant, DirectInjection, IndirectInjection, PromptExtraction, Medical, InScope).
        /// </summary>
        public string Category { get; set; } = String.Empty;

        /// <summary>
        /// What makes the case hard (for example disguised, pasted, near-miss-macro).
        /// </summary>
        public string Type { get; set; } = String.Empty;

        /// <summary>
        /// tune or holdout. Holdout cases are reported separately so tuning cannot overfit them silently.
        /// </summary>
        public string Split { get; set; } = ScopeBenchmark.Tune;

        /// <summary>
        /// The member's message.
        /// </summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>
        /// Fetched document or web page text given to the model as retrieved content (indirect injection), or null.
        /// </summary>
        public string? Document { get; set; } = null;

        /// <summary>
        /// Expected outcome per layer. Never null.
        /// </summary>
        public ScopeCaseExpectation Expect
        {
            get { return _Expect; }
            set { _Expect = value ?? new ScopeCaseExpectation(); }
        }

        #endregion

        #region Private-Members

        private ScopeCaseExpectation _Expect = new ScopeCaseExpectation();

        #endregion
    }
}
