namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The effective events configuration clients render from.
    /// </summary>
    public class PublicEventsConfig
    {
        #region Public-Members

        /// <summary>
        /// Active (not retired) categories in admin order.
        /// </summary>
        public List<PublicOption> Categories { get; set; } = new List<PublicOption>();

        /// <summary>
        /// Members may propose events through the review queue.
        /// </summary>
        public bool Proposals { get; set; } = true;

        /// <summary>
        /// Personal calendar subscription feeds are available.
        /// </summary>
        public bool CalendarFeedEnabled { get; set; } = true;

        /// <summary>
        /// Default reminder offsets for new events, in minutes.
        /// </summary>
        public List<int> DefaultReminderOffsetsMinutes { get; set; } = new List<int> { 1440, 120 };

        /// <summary>
        /// The anonymous public event listings are served (events.publicListingsEnabled). Default false.
        /// Clients skip the public routes while this is false.
        /// </summary>
        public bool PublicListingsEnabled { get; set; } = false;

        #endregion
    }
}
