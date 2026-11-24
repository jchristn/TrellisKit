namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A client-reported failure of an update with its private message (docs/LIVE_UPDATES_CONTRACT.md as-built notes).
    /// </summary>
    public class UpdateFailureView
    {
        /// <summary>
        /// Event id (uev_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Event type.
        /// </summary>
        public UpdateEventTypeEnum Type { get; set; } = UpdateEventTypeEnum.LaunchFailed;

        /// <summary>
        /// Bounded error code.
        /// </summary>
        public UpdateErrorCodeEnum? ErrorCode { get; set; } = null;

        /// <summary>
        /// Message.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Whether a stack trace was reported.
        /// </summary>
        public bool HasStackTrace { get; set; } = false;

        /// <summary>
        /// Client time.
        /// </summary>
        public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Server time.
        /// </summary>
        public DateTime ReceivedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
