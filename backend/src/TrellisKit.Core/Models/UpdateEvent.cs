namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A client-reported live update event (table updateevents).
    /// </summary>
    public class UpdateEvent
    {
        /// <summary>
        /// Identifier (uev_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// HMAC hex of the install id.
        /// </summary>
        public string InstallIdHash { get; set; } = String.Empty;

        /// <summary>
        /// Update UUID or null.
        /// </summary>
        public string? UpdateUuid { get; set; } = null;

        /// <summary>
        /// Event type.
        /// </summary>
        public UpdateEventTypeEnum Type { get; set; } = UpdateEventTypeEnum.CheckFailed;

        /// <summary>
        /// Bounded error code, or null.
        /// </summary>
        public UpdateErrorCodeEnum? ErrorCode { get; set; } = null;

        /// <summary>
        /// Launch duration, or null.
        /// </summary>
        public int? LaunchDurationMs { get; set; } = null;

        /// <summary>
        /// Client time.
        /// </summary>
        public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Server time.
        /// </summary>
        public DateTime ReceivedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Error message of a failure event (at most 500 characters), or null. Stored privately.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Stack trace of a failure event (at most 16384 characters), or null. Stored privately for symbolication.
        /// </summary>
        public string? StackTrace { get; set; } = null;
    }
}
