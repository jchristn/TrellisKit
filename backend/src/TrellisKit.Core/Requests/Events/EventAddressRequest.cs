namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Address of an event location (docs/PHASE4_CONTRACT.md section 2.1).
    /// </summary>
    public class EventAddressRequest
    {
        #region Public-Members

        /// <summary>
        /// Line 1 (0 to 200).
        /// </summary>
        public string? Line1 { get; set; } = null;

        /// <summary>
        /// Line 2 (0 to 200).
        /// </summary>
        public string? Line2 { get; set; } = null;

        /// <summary>
        /// Locality (0 to 100).
        /// </summary>
        public string? Locality { get; set; } = null;

        /// <summary>
        /// Region (0 to 100).
        /// </summary>
        public string? Region { get; set; } = null;

        /// <summary>
        /// Postal code (0 to 20).
        /// </summary>
        public string? PostalCode { get; set; } = null;

        /// <summary>
        /// ISO 3166-1 alpha-2 (required when any address field is set).
        /// </summary>
        public string? CountryCode { get; set; } = null;

        #endregion
    }
}
