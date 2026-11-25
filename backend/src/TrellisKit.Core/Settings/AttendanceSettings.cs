namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Attendance check-in settings (docs/TRELLISKIT.md, Ranks, curricula, and attendance).
    /// </summary>
    public class AttendanceSettings
    {
        #region Public-Members

        /// <summary>
        /// Minutes before an event or session start when member check-in opens. Key attendance.checkInWindowBeforeMinutes.
        /// Default 60. Minimum 0, maximum 720.
        /// </summary>
        public int CheckInWindowBeforeMinutes
        {
            get { return _CheckInWindowBeforeMinutes; }
            set { _CheckInWindowBeforeMinutes = Math.Clamp(value, 0, 720); }
        }

        /// <summary>
        /// Minutes after an event or session start when member check-in closes. Key attendance.checkInWindowAfterMinutes.
        /// Default 120. Minimum 0, maximum 720.
        /// </summary>
        public int CheckInWindowAfterMinutes
        {
            get { return _CheckInWindowAfterMinutes; }
            set { _CheckInWindowAfterMinutes = Math.Clamp(value, 0, 720); }
        }

        /// <summary>
        /// Seconds each rotating check-in code is shown (the previous code is accepted for one more period). Key
        /// attendance.codeRotationSeconds. Default 60. Minimum 15, maximum 600.
        /// </summary>
        public int CodeRotationSeconds
        {
            get { return _CodeRotationSeconds; }
            set { _CodeRotationSeconds = Math.Clamp(value, 15, 600); }
        }

        #endregion

        #region Private-Members

        private int _CheckInWindowBeforeMinutes = 60;
        private int _CheckInWindowAfterMinutes = 120;
        private int _CodeRotationSeconds = 60;

        #endregion
    }
}
