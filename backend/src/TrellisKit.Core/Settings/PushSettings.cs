namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Push delivery settings (contract section 8.8).
    /// </summary>
    public class PushSettings
    {
        #region Public-Members

        /// <summary>
        /// Turn off to stop every push; notifications are still created. Default true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Expo push access token. Empty sends unauthenticated Expo pushes. Default empty. Never null.
        /// </summary>
        public string ExpoAccessToken
        {
            get { return _ExpoAccessToken; }
            set { _ExpoAccessToken = value ?? String.Empty; }
        }

        /// <summary>
        /// Pushes a member may receive each hour. Default 10. Minimum 1, maximum 1000.
        /// </summary>
        public int MaxPerUserPerHour
        {
            get { return _MaxPerUserPerHour; }
            set { _MaxPerUserPerHour = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// How long after sending the worker checks Expo receipts. Default 15. Minimum 1, maximum 1440.
        /// </summary>
        public int ReceiptDelayMinutes
        {
            get { return _ReceiptDelayMinutes; }
            set { _ReceiptDelayMinutes = Math.Clamp(value, 1, 1440); }
        }

        /// <summary>
        /// How often the push worker sends pending pushes. Default 5. Minimum 1, maximum 300.
        /// </summary>
        public int SendIntervalSeconds
        {
            get { return _SendIntervalSeconds; }
            set { _SendIntervalSeconds = Math.Clamp(value, 1, 300); }
        }

        /// <summary>
        /// Push token registrations a member may make each hour. Default 20. Minimum 1, maximum 1000.
        /// </summary>
        public int RegistrationsPerHour
        {
            get { return _RegistrationsPerHour; }
            set { _RegistrationsPerHour = Math.Clamp(value, 1, 1000); }
        }

        #endregion

        #region Private-Members

        private string _ExpoAccessToken = String.Empty;
        private int _MaxPerUserPerHour = 10;
        private int _ReceiptDelayMinutes = 15;
        private int _SendIntervalSeconds = 5;
        private int _RegistrationsPerHour = 20;

        #endregion
    }
}
