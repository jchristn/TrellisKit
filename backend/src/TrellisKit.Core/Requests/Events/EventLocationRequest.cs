namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Location of an InPerson or Hybrid event: a directory venue, or a name and an address.
    /// </summary>
    public class EventLocationRequest
    {
        #region Public-Members

        /// <summary>
        /// Directory venue (its address is copied), or null.
        /// </summary>
        public string? VenueId { get; set; } = null;

        /// <summary>
        /// Place name (0 to 120), or null.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Address, or null.
        /// </summary>
        public EventAddressRequest? Address { get; set; } = null;

        /// <summary>
        /// Exact (default), Approximate, or LocalityOnly.
        /// </summary>
        public string? AddressVisibility { get; set; } = null;

        /// <summary>
        /// Manual position, or null.
        /// </summary>
        public EventGeoRequest? Geo { get; set; } = null;

        #endregion
    }
}
