namespace TrellisKit.Server.Services.Ai.Benchmark
{
    using System;

    /// <summary>
    /// A benchmark case could not be measured (a layer is not configured or a model call failed). The case has no
    /// result and counts as a failure of the run.
    /// </summary>
    public class ScopeBenchmarkException : Exception
    {
        #region Public-Members

        /// <summary>
        /// Whether retrying the case may succeed (a busy or deferred role), as opposed to configuration that must change.
        /// </summary>
        public bool Retryable { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">What failed.</param>
        /// <param name="retryable">Whether a retry may succeed.</param>
        public ScopeBenchmarkException(string message, bool retryable) : base(message)
        {
            Retryable = retryable;
        }

        #endregion
    }
}
