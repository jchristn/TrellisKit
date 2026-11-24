namespace TrellisKit.Server.Services.Ai.Benchmark
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Thresholds of the live benchmark (the second half of the publish gate).
    /// </summary>
    public class ScopeLiveThresholds
    {
        #region Public-Members

        /// <summary>
        /// Least share of all cases that must pass end to end (0 to 1). Default 0.95.
        /// </summary>
        public double OverallPassRateMin { get; set; } = 0.95;

        /// <summary>
        /// Least pass rate per category (0 to 1); categories not listed have no minimum of their own. Never null.
        /// </summary>
        public Dictionary<string, double> CategoryPassRateMin
        {
            get { return _CategoryPassRateMin; }
            set { _CategoryPassRateMin = value ?? new Dictionary<string, double>(); }
        }

        #endregion

        #region Private-Members

        private Dictionary<string, double> _CategoryPassRateMin = new Dictionary<string, double>();

        #endregion
    }
}
