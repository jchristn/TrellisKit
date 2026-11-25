namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Sign-up code settings.
    /// </summary>
    public class SignupCodeSettings
    {
        #region Public-Members

        /// <summary>
        /// Revoke future free membership redemptions when a subscription is refunded. Default false.
        /// </summary>
        public bool CancelQueuedOnRefund { get; set; } = false;


        /// <summary>
        /// Validate and redeem attempts allowed per IP and per account each hour. Default 10. Minimum 1, maximum 1000.
        /// </summary>
        public int AttemptsPerHour
        {
            get { return _AttemptsPerHour; }
            set { _AttemptsPerHour = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Length of generated codes. Default 10. Minimum 8, maximum 32.
        /// </summary>
        public int GeneratedLength
        {
            get { return _GeneratedLength; }
            set { _GeneratedLength = Math.Clamp(value, 8, 32); }
        }

        /// <summary>
        /// Days before a code membership ends that the first reminder email is sent. Default 14. Minimum 2, maximum 60.
        /// </summary>
        public int FirstReminderDays
        {
            get { return _FirstReminderDays; }
            set { _FirstReminderDays = Math.Clamp(value, 2, 60); }
        }

        /// <summary>
        /// Days before a code membership ends that the final reminder email is sent. Default 3. Minimum 1, maximum 30.
        /// </summary>
        public int FinalReminderDays
        {
            get { return _FinalReminderDays; }
            set { _FinalReminderDays = Math.Clamp(value, 1, 30); }
        }

        #endregion

        #region Private-Members

        private int _AttemptsPerHour = 10;
        private int _GeneratedLength = 10;
        private int _FirstReminderDays = 14;
        private int _FinalReminderDays = 3;

        #endregion
    }
}
