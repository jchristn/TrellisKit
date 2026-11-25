namespace TrellisKit.Server.Services
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A resolved check-in target occurrence: an event, or a venue schedule session on one date, with its start instant.
    /// </summary>
    public class CheckInTarget
    {
        #region Public-Members

        /// <summary>
        /// Event or VenueSession.
        /// </summary>
        public AttendanceTargetTypeEnum TargetType { get; set; } = AttendanceTargetTypeEnum.Event;

        /// <summary>
        /// Event identifier, or venue session identifier ({venueId}:{sessionIndex}).
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Occurrence date (yyyy-MM-dd) in the target's time zone.
        /// </summary>
        public string OccurrenceDate { get; set; } = String.Empty;

        /// <summary>
        /// Start of the occurrence.
        /// </summary>
        public DateTime StartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// IANA time zone of the target.
        /// </summary>
        public string TimeZone { get; set; } = "UTC";

        #endregion
    }
}
