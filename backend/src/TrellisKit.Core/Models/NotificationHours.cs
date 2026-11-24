namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A row of notificationhours: the member's push window and pause switch. A missing row means the defaults.
    /// </summary>
    public class NotificationHours
    {
        /// <summary>
        /// Member identifier.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Whether the window applies. False means pushes may arrive at any time. Default true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Window start in minutes after local midnight. Default 420 (07:00). Minimum 0, maximum 1439.
        /// </summary>
        public int StartMinute
        {
            get { return _StartMinute; }
            set { _StartMinute = Math.Clamp(value, 0, 1439); }
        }

        /// <summary>
        /// Window end in minutes after local midnight. Default 1320 (22:00). Minimum 0, maximum 1439. Equal to the start
        /// means all day; smaller than the start means the window crosses midnight.
        /// </summary>
        public int EndMinute
        {
            get { return _EndMinute; }
            set { _EndMinute = Math.Clamp(value, 0, 1439); }
        }

        /// <summary>
        /// IANA time zone, or null when the client has not sent one (the window is then evaluated in UTC).
        /// </summary>
        public string? TimeZone { get; set; } = null;

        /// <summary>
        /// Every push is paused. Default false.
        /// </summary>
        public bool PushPaused { get; set; } = false;

        /// <summary>
        /// Last change (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        private int _StartMinute = 420;
        private int _EndMinute = 1320;
    }
}
