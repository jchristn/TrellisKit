namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Where an event takes place.
    /// </summary>
    public class EventLocationView
    {
        #region Public-Members

        /// <summary>
        /// Directory venue the caller can read, or null.
        /// </summary>
        public EventVenueView? Venue { get; set; } = null;

        /// <summary>
        /// Place name, or null.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Address, or null.
        /// </summary>
        public EventAddressView? Address { get; set; } = null;

        /// <summary>
        /// Exact, Approximate, or LocalityOnly.
        /// </summary>
        public string AddressVisibility { get; set; } = "Exact";

        /// <summary>
        /// Position, or null.
        /// </summary>
        public EventGeoView? Geo { get; set; } = null;

        /// <summary>
        /// Distance from near, or null.
        /// </summary>
        public double? DistanceKm { get; set; } = null;

        #endregion
    }
}
