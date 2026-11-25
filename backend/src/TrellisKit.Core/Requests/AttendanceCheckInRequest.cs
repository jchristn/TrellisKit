namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// POST /api/v1/attendance/check-in.
    /// </summary>
    public class AttendanceCheckInRequest
    {
        #region Public-Members

        /// <summary>
        /// Event or VenueSession.
        /// </summary>
        public string? TargetType { get; set; } = null;

        /// <summary>
        /// Event identifier, or venue session identifier ({venueId}:{sessionIndex}).
        /// </summary>
        public string? TargetId { get; set; } = null;

        /// <summary>
        /// yyyy-MM-dd in the target's time zone (required for VenueSession; for Event the event's start date).
        /// </summary>
        public string? OccurrenceDate { get; set; } = null;

        /// <summary>
        /// The target's rotating code (members checking themselves in).
        /// </summary>
        public string? Code { get; set; } = null;

        /// <summary>
        /// Member to check in (staff only; omit or send your own id to check yourself in).
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// QR or SelfWithCode (default) for members; staff check-ins are Staff.
        /// </summary>
        public string? Method { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
