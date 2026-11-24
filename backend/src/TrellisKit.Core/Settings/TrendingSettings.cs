namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Trending settings (docs/PHASE3_CONTRACT.md sections 5 and 15).
    /// </summary>
    public class TrendingSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether the trending worker runs. Default true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Minutes between recomputes. Default 10. Minimum 1, maximum 1440.
        /// </summary>
        public int IntervalMinutes
        {
            get { return _IntervalMinutes; }
            set { _IntervalMinutes = Math.Clamp(value, 1, 1440); }
        }

        /// <summary>
        /// Threads active within this many days are scored; others score 0. Default 7. Minimum 1, maximum 90.
        /// </summary>
        public int WindowDays
        {
            get { return _WindowDays; }
            set { _WindowDays = Math.Clamp(value, 1, 90); }
        }

        /// <summary>
        /// Threads per recompute batch. Default 500. Minimum 50, maximum 5000.
        /// </summary>
        public int BatchSize
        {
            get { return _BatchSize; }
            set { _BatchSize = Math.Clamp(value, 50, 5000); }
        }

        /// <summary>
        /// Weight of reactions. Default 1. Minimum 0, maximum 100.
        /// </summary>
        public double ReactionWeight
        {
            get { return _ReactionWeight; }
            set { _ReactionWeight = Math.Clamp(value, 0.0, 100.0); }
        }

        /// <summary>
        /// Weight of visible comments. Default 2. Minimum 0, maximum 100.
        /// </summary>
        public double CommentWeight
        {
            get { return _CommentWeight; }
            set { _CommentWeight = Math.Clamp(value, 0.0, 100.0); }
        }

        /// <summary>
        /// Weight of distinct commenters other than the author. Default 3. Minimum 0, maximum 100.
        /// </summary>
        public double CommenterWeight
        {
            get { return _CommenterWeight; }
            set { _CommenterWeight = Math.Clamp(value, 0.0, 100.0); }
        }

        /// <summary>
        /// Hours added to a thread's age so new threads do not divide by zero. Default 2. Minimum 0.1, maximum 48.
        /// </summary>
        public double AgeOffsetHours
        {
            get { return _AgeOffsetHours; }
            set { _AgeOffsetHours = Math.Clamp(value, 0.1, 48.0); }
        }

        /// <summary>
        /// How fast scores decay with age. Default 1.5. Minimum 0.1, maximum 5.
        /// </summary>
        public double Gravity
        {
            get { return _Gravity; }
            set { _Gravity = Math.Clamp(value, 0.1, 5.0); }
        }

        #endregion

        #region Private-Members

        private int _IntervalMinutes = 10;
        private int _WindowDays = 7;
        private int _BatchSize = 500;
        private double _ReactionWeight = 1.0;
        private double _CommentWeight = 2.0;
        private double _CommenterWeight = 3.0;
        private double _AgeOffsetHours = 2.0;
        private double _Gravity = 1.5;

        #endregion
    }
}
