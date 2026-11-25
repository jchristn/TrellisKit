namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One Up next card item: an upcoming event the member RSVPed Going to (kind Event). Engines with bookings add other
    /// kinds through their own section provider.
    /// </summary>
    public class TrainingUpNextItem : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Item kind (Event).
        /// </summary>
        public string Kind { get; set; } = "Event";

        /// <summary>
        /// Target identifier (the event).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// UTC start.
        /// </summary>
        public DateTime StartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC end.
        /// </summary>
        public DateTime EndUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// IANA time zone of the item.
        /// </summary>
        public string TimeZone { get; set; } = "UTC";

        /// <summary>
        /// Short location (venue or place name, locality, or "Online"), or null.
        /// </summary>
        public string? LocationSummary { get; set; } = null;

        /// <summary>
        /// Client path of the item (for example /events/{id}).
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// Labels of the target visible to the caller.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags of the target visible to the caller.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion
    }
}
