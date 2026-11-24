namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Job engine settings (docs/INTEGRATION.md).
    /// </summary>
    public class JobEngineSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether the scheduler and item processing run on this instance. Default true. Off stops scheduling and
        /// processing; administrators can still read history.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Seconds between scheduler and processing passes. Default 15, minimum 5, maximum 600.
        /// </summary>
        public int TickSeconds
        {
            get { return _TickSeconds; }
            set { _TickSeconds = Math.Clamp(value, 5, 600); }
        }

        /// <summary>
        /// Cost cap across every job of a property per calendar month (UTC), in US dollars. Default 2000, minimum 0
        /// (no paid work), maximum 1000000. Scheduled and manual runs are skipped, and running runs stop claiming items,
        /// once the month's cost reaches the cap.
        /// </summary>
        public double MonthlyBudgetUsd
        {
            get { return _MonthlyBudgetUsd; }
            set { _MonthlyBudgetUsd = Math.Clamp(value, 0, 1000000); }
        }

        /// <summary>
        /// Queued runs a pass plans. Default 5, minimum 1, maximum 100.
        /// </summary>
        public int MaxRunsPerPass
        {
            get { return _MaxRunsPerPass; }
            set { _MaxRunsPerPass = Math.Clamp(value, 1, 100); }
        }

        /// <summary>
        /// Minutes an item claim lasts before another worker may retry it. Default 15, minimum 1, maximum 1440.
        /// </summary>
        public int ItemLockMinutes
        {
            get { return _ItemLockMinutes; }
            set { _ItemLockMinutes = Math.Clamp(value, 1, 1440); }
        }

        /// <summary>
        /// Base retry delay in seconds, doubled per attempt with jitter. Default 30, minimum 1, maximum 3600.
        /// </summary>
        public int RetryBaseSeconds
        {
            get { return _RetryBaseSeconds; }
            set { _RetryBaseSeconds = Math.Clamp(value, 1, 3600); }
        }

        /// <summary>
        /// Longest retry delay in seconds. Default 3600, minimum 1, maximum 86400.
        /// </summary>
        public int RetryMaxSeconds
        {
            get { return _RetryMaxSeconds; }
            set { _RetryMaxSeconds = Math.Clamp(value, 1, 86400); }
        }

        /// <summary>
        /// Whether this instance claims items that need a CLI agent harness. Default false; set true only on worker
        /// instances whose image carries the CLIs (harnesses never run in request paths).
        /// </summary>
        public bool RunHarnesses { get; set; } = false;

        /// <summary>
        /// Longest a single CLI agent harness call may run, in seconds, before its process tree is killed. Default 600,
        /// minimum 30, maximum 3600. Keep it below jobs.itemLockMinutes so an attempt ends before its claim expires.
        /// </summary>
        public int HarnessTimeoutSeconds
        {
            get { return _HarnessTimeoutSeconds; }
            set { _HarnessTimeoutSeconds = Math.Clamp(value, 30, 3600); }
        }

        /// <summary>
        /// Most job tool calls one agent call (harness or API tool loop) may make. Default 50, minimum 1, maximum 500.
        /// Calls past the limit are refused with an error the agent can read.
        /// </summary>
        public int HarnessMaxToolCalls
        {
            get { return _HarnessMaxToolCalls; }
            set { _HarnessMaxToolCalls = Math.Clamp(value, 1, 500); }
        }

        /// <summary>
        /// Extra environment variable names (comma or space separated) passed to harness processes on top of the fixed
        /// allowlist (PATH, locale, proxy, CA bundle, and each vendor's API key variables). Default empty. Every other
        /// variable of the server process, including every TK_ setting, is removed from the harness environment.
        /// Never null.
        /// </summary>
        public string HarnessPassEnvironment
        {
            get { return _HarnessPassEnvironment; }
            set { _HarnessPassEnvironment = value ?? String.Empty; }
        }

        /// <summary>
        /// Days finished runs and their items are kept. Default 90, minimum 7, maximum 3650.
        /// </summary>
        public int RetentionDays
        {
            get { return _RetentionDays; }
            set { _RetentionDays = Math.Clamp(value, 7, 3650); }
        }

        #endregion

        #region Private-Members

        private int _TickSeconds = 15;
        private double _MonthlyBudgetUsd = 2000;
        private int _MaxRunsPerPass = 5;
        private int _ItemLockMinutes = 15;
        private int _RetryBaseSeconds = 30;
        private int _RetryMaxSeconds = 3600;
        private int _RetentionDays = 90;
        private int _HarnessTimeoutSeconds = 600;
        private int _HarnessMaxToolCalls = 50;
        private string _HarnessPassEnvironment = String.Empty;

        #endregion
    }
}
