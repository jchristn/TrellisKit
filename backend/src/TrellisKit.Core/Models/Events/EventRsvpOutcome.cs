namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Result of an RSVP transaction: the decision, the caller's RSVP afterwards, and the RSVPs promoted from the waitlist.
    /// </summary>
    public class EventRsvpOutcome
    {
        #region Public-Members

        /// <summary>
        /// Decision.
        /// </summary>
        public EventRsvpDecisionEnum Decision { get; set; } = EventRsvpDecisionEnum.Missing;

        /// <summary>
        /// The RSVP afterwards, or null.
        /// </summary>
        public EventRsvp? Rsvp { get; set; } = null;

        /// <summary>
        /// Members promoted from the waitlist in this transaction, in promotion order.
        /// </summary>
        public List<string> PromotedUserIds { get; set; } = new List<string>();

        #endregion
    }
}
