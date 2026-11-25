namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A member's check-in to an event or a venue session on one date (table attendance, id prefix att_).
    /// </summary>
    public class AttendanceView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (att_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Member summary (staff lists), or null.
        /// </summary>
        public UserSummary? User { get; set; } = null;

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
        /// Check-in time.
        /// </summary>
        public DateTime CheckedInUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// QR, Staff, or SelfWithCode.
        /// </summary>
        public AttendanceMethodEnum Method { get; set; } = AttendanceMethodEnum.SelfWithCode;

        /// <summary>
        /// Staff member who checked the member in, or null.
        /// </summary>
        public string? CheckedInBy { get; set; } = null;

        /// <summary>
        /// Creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
