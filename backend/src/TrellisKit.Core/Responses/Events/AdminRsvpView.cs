namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An RSVP for event staff and hosts (AdminRsvp).
    /// </summary>
    public class AdminRsvpView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (rsv_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Event.
        /// </summary>
        public string EventId { get; set; } = String.Empty;

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
        /// Hidden from attendee lists.
        /// </summary>
        public bool HideFromAttendeeLists { get; set; } = false;

        /// <summary>
        /// When waitlisted, or null.
        /// </summary>
        public DateTime? WaitlistedUtc { get; set; } = null;

        /// <summary>
        /// When promoted, or null.
        /// </summary>
        public DateTime? PromotedUtc { get; set; } = null;

        /// <summary>
        /// Check-in time, or null.
        /// </summary>
        public DateTime? CheckedInUtc { get; set; } = null;

        /// <summary>
        /// Who checked the member in, or null.
        /// </summary>
        public string? CheckedInByUserId { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

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
