namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Outcome of an RSVP write decided inside its transaction.
    /// </summary>
    public enum EventRsvpDecisionEnum
    {
        /// <summary>
        /// The RSVP is Going.
        /// </summary>
        Going,

        /// <summary>
        /// The RSVP is Interested.
        /// </summary>
        Interested,

        /// <summary>
        /// The event was full; the RSVP is Waitlisted.
        /// </summary>
        Waitlisted,

        /// <summary>
        /// The event was full and has no waitlist; nothing was written.
        /// </summary>
        Full,

        /// <summary>
        /// The event is not open for RSVPs (not published, canceled, or started); nothing was written.
        /// </summary>
        Closed,

        /// <summary>
        /// The RSVP was deleted.
        /// </summary>
        Removed,

        /// <summary>
        /// There was no RSVP to change.
        /// </summary>
        Missing
    }
}
