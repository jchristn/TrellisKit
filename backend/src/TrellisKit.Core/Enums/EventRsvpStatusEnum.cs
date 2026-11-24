namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Status of an RSVP (docs/PHASE4_CONTRACT.md section 4.3).
    /// </summary>
    public enum EventRsvpStatusEnum
    {
        /// <summary>
        /// Attending; counts against capacity with its guests.
        /// </summary>
        Going,

        /// <summary>
        /// Interested; never counts against capacity.
        /// </summary>
        Interested,

        /// <summary>
        /// Wants to go but the event is full; promoted in order when space opens.
        /// </summary>
        Waitlisted
    }
}
