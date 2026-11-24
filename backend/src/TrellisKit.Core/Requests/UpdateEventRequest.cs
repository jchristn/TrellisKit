namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One client-reported update event.
    /// </summary>
    public class UpdateEventRequest
    {
        /// <summary>
        /// Event type (UpdateEventTypeEnum name).
        /// </summary>
        public string Type { get; set; } = String.Empty;

        /// <summary>
        /// Update UUID, or null.
        /// </summary>
        public string? UpdateId { get; set; } = null;

        /// <summary>
        /// Bounded error code, or null.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// Client time.
        /// </summary>
        public DateTime? OccurredUtc { get; set; } = null;

        /// <summary>
        /// Launch duration, or null.
        /// </summary>
        public int? LaunchDurationMs { get; set; } = null;

        /// <summary>
        /// Error message of a failure event (at most 500 characters; longer text is truncated), or null.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Stack trace of a failure event (at most 16384 characters; longer text is truncated), or null.
        /// </summary>
        public string? StackTrace { get; set; } = null;
    }
}
