namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An attendee of an event.
    /// </summary>
    public class EventAttendeeView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// RSVP identifier (rsv_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Member.
        /// </summary>
        public UserSummary User { get; set; } = new UserSummary();

        /// <summary>
        /// Status.
        /// </summary>
        public EventRsvpStatusEnum Status { get; set; } = EventRsvpStatusEnum.Going;

        /// <summary>
        /// Guests.
        /// </summary>
        public int GuestCount { get; set; } = 0;

        /// <summary>
        /// Waitlist position, or null.
        /// </summary>
        public int? Position { get; set; } = null;

        /// <summary>
        /// Check-in time (staff and hosts), or null.
        /// </summary>
        public DateTime? CheckedInUtc { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion
    }
}
