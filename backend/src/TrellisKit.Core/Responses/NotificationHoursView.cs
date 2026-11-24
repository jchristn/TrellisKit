namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Notification hours in the settings document (Phase 2 contract 8.6).
    /// </summary>
    public class NotificationHoursView
    {
        /// <summary>
        /// Whether the window applies.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Window start, HH:mm local.
        /// </summary>
        public string Start { get; set; } = "07:00";

        /// <summary>
        /// Window end, HH:mm local.
        /// </summary>
        public string End { get; set; } = "22:00";

        /// <summary>
        /// IANA time zone, or null until the client sends one (the window is then evaluated in UTC).
        /// </summary>
        public string? TimeZone { get; set; } = null;
    }
}
