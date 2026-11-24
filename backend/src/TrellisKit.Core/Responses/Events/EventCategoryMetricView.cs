namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Events and RSVPs of one category.
    /// </summary>
    public class EventCategoryMetricView
    {
        #region Public-Members

        /// <summary>
        /// Category key (empty for none).
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Events.
        /// </summary>
        public long Events { get; set; } = 0;

        /// <summary>
        /// Going RSVPs.
        /// </summary>
        public long Rsvps { get; set; } = 0;

        #endregion
    }
}
