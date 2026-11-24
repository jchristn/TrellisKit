namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// An event address (docs/PHASE4_CONTRACT.md section 2.1).
    /// </summary>
    public class EventAddressView
    {
        #region Public-Members

        /// <summary>
        /// Line 1 (null unless the caller may see the exact address).
        /// </summary>
        public string? Line1 { get; set; } = null;

        /// <summary>
        /// Line 2.
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
        /// Postal code.
        /// </summary>
        public string? PostalCode { get; set; } = null;

        /// <summary>
        /// Country code.
        /// </summary>
        public string? CountryCode { get; set; } = null;

        /// <summary>
        /// Server-built display text.
        /// </summary>
        public string Formatted { get; set; } = String.Empty;

        #endregion
    }
}
