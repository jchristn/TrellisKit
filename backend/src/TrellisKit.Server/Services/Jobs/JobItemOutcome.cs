namespace TrellisKit.Server.Services.Jobs
{
    using System;

    /// <summary>
    /// The result of one item attempt.
    /// </summary>
    public class JobItemOutcome
    {
        #region Public-Members

        /// <summary>
        /// Whether the attempt succeeded.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// For a failure: whether another attempt may succeed. Default true.
        /// </summary>
        public bool Retryable { get; set; } = true;

        /// <summary>
        /// Output (JSON), or null; at most 64 KB.
        /// </summary>
        public string? Result { get; set; } = null;

        /// <summary>
        /// Failure message, or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Cost of the attempt in micro-dollars. Default 0, minimum 0.
        /// </summary>
        public long CostMicros
        {
            get { return _CostMicros; }
            set { _CostMicros = Math.Max(0, value); }
        }

        #endregion

        #region Private-Members

        private long _CostMicros = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// A successful attempt.
        /// </summary>
        /// <param name="result">Output JSON, or null.</param>
        /// <param name="costMicros">Cost in micro-dollars.</param>
        /// <returns>Outcome.</returns>
        public static JobItemOutcome Ok(string? result = null, long costMicros = 0)
        {
            return new JobItemOutcome { Success = true, Result = result, CostMicros = costMicros };
        }

        /// <summary>
        /// A failed attempt.
        /// </summary>
        /// <param name="error">Message.</param>
        /// <param name="retryable">Whether another attempt may succeed.</param>
        /// <param name="costMicros">Cost in micro-dollars.</param>
        /// <returns>Outcome.</returns>
        public static JobItemOutcome Fail(string error, bool retryable = true, long costMicros = 0)
        {
            return new JobItemOutcome { Success = false, Retryable = retryable, Error = error, CostMicros = costMicros };
        }

        #endregion
    }
}
