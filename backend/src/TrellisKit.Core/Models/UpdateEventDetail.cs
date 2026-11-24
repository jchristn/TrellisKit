namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The private message and stack trace of a client-reported failure event (table updateeventdetails).
    /// </summary>
    public class UpdateEventDetail
    {
        /// <summary>
        /// Event id (uev_).
        /// </summary>
        public string EventId { get; set; } = String.Empty;

        /// <summary>
        /// Update UUID, or null.
        /// </summary>
        public string? UpdateUuid { get; set; } = null;

        /// <summary>
        /// Event type.
        /// </summary>
        public UpdateEventTypeEnum Type { get; set; } = UpdateEventTypeEnum.LaunchFailed;

        /// <summary>
        /// Bounded error code, or null.
        /// </summary>
        public UpdateErrorCodeEnum? ErrorCode { get; set; } = null;

        /// <summary>
        /// Error message, or null.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Stack trace, or null.
        /// </summary>
        public string? StackTrace { get; set; } = null;

        /// <summary>
        /// Client time.
        /// </summary>
        public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Server time.
        /// </summary>
        public DateTime ReceivedUtc { get; set; } = DateTime.UtcNow;
    }
}
