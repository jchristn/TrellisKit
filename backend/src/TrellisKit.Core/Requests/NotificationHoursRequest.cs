namespace TrellisKit.Core.Requests
{
    /// <summary>
    /// Notification hours changes; omitted fields are unchanged.
    /// </summary>
    public class NotificationHoursRequest
    {
        /// <summary>
        /// Whether the window applies, or null.
        /// </summary>
        public bool? Enabled { get; set; } = null;

        /// <summary>
        /// Start, HH:mm, or null.
        /// </summary>
        public string? Start { get; set; } = null;

        /// <summary>
        /// End, HH:mm, or null.
        /// </summary>
        public string? End { get; set; } = null;

        /// <summary>
        /// IANA time zone, or null.
        /// </summary>
        public string? TimeZone { get; set; } = null;
    }
}
