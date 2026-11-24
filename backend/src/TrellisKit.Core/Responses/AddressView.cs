namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// A venue address as the caller may see it (docs/PHASE4_CONTRACT.md section 2.1).
    /// </summary>
    public class AddressView
    {
        #region Public-Members

        /// <summary>
        /// Line 1 (null unless the caller sees the exact address).
        /// </summary>
        public string? Line1 { get; set; } = null;

        /// <summary>
        /// Line 2 (null unless the caller sees the exact address).
        /// </summary>
        public string? Line2 { get; set; } = null;

        /// <summary>
        /// Locality.
        /// </summary>
        public string? Locality { get; set; } = null;

        /// <summary>
        /// Region.
        /// </summary>
        public string? Region { get; set; } = null;

        /// <summary>
        /// Postal code (null unless the caller sees the exact address).
        /// </summary>
        public string? PostalCode { get; set; } = null;

        /// <summary>
        /// ISO 3166-1 alpha-2 country code.
        /// </summary>
        public string? CountryCode { get; set; } = null;

        /// <summary>
        /// Display form built by the server.
        /// </summary>
        public string? Formatted { get; set; } = null;

        #endregion
    }
}
