namespace TrellisKit.Server.Services
{
    using System;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Models;

    /// <summary>
    /// A normalized venue enumeration: the request and its parsed filters.
    /// </summary>
    public class VenueQuery
    {
        #region Public-Members

        /// <summary>
        /// Normalized request.
        /// </summary>
        public EnumerationRequest Request { get; set; } = new EnumerationRequest();

        /// <summary>
        /// Parsed filters.
        /// </summary>
        public VenueFilter Filter { get; set; } = new VenueFilter();

        #endregion
    }
}
