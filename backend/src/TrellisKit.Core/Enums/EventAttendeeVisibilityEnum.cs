namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Who may see an event's attendee list (docs/PHASE4_CONTRACT.md section 1.2).
    /// </summary>
    public enum EventAttendeeVisibilityEnum
    {
        /// <summary>
        /// Anyone who can read the event.
        /// </summary>
        Everyone,

        /// <summary>
        /// Members with a Going RSVP (and event staff and hosts).
        /// </summary>
        Attendees,

        /// <summary>
        /// Event staff and hosts only.
        /// </summary>
        Staff
    }
}
