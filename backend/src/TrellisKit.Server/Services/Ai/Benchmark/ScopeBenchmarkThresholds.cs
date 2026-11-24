namespace TrellisKit.Server.Services.Ai.Benchmark
{
    using System;

    /// <summary>
    /// Thresholds of the scope benchmark, versioned with the dataset.
    /// </summary>
    public class ScopeBenchmarkThresholds
    {
        #region Public-Members

        /// <summary>
        /// Deterministic gate thresholds. Never null.
        /// </summary>
        public ScopeDeterministicThresholds Deterministic
        {
            get { return _Deterministic; }
            set { _Deterministic = value ?? new ScopeDeterministicThresholds(); }
        }

        /// <summary>
        /// Live benchmark thresholds. Never null.
        /// </summary>
        public ScopeLiveThresholds Live
        {
            get { return _Live; }
            set { _Live = value ?? new ScopeLiveThresholds(); }
        }

        #endregion

        #region Private-Members

        private ScopeDeterministicThresholds _Deterministic = new ScopeDeterministicThresholds();
        private ScopeLiveThresholds _Live = new ScopeLiveThresholds();

        #endregion
    }
}
