namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A directory venue on an event (VenueSummary subset).
    /// </summary>
    public class EventVenueView
    {
        #region Public-Members

        /// <summary>
        /// Identifier (ven_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Slug.
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Locality.
        /// </summary>
        public string? Locality { get; set; } = null;

        /// <summary>
        /// Region.
        /// </summary>
        public string? Region { get; set; } = null;

        /// <summary>
        /// Country code.
        /// </summary>
        public string? CountryCode { get; set; } = null;

        #endregion
    }
}
