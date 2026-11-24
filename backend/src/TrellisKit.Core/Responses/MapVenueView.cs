namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A venue on the map (docs/PHASE4_CONTRACT.md section 3.3).
    /// </summary>
    public class MapVenueView
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Slug.
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Position (Exact or Approximate).
        /// </summary>
        public GeoView Geo { get; set; } = new GeoView();

        /// <summary>
        /// Program keys.
        /// </summary>
        public List<string> Programs { get; set; } = new List<string>();

        /// <summary>
        /// Verified.
        /// </summary>
        public bool IsVerified { get; set; } = false;

        /// <summary>
        /// Thumbnail of the first photo, or null.
        /// </summary>
        public string? CoverThumbUrl { get; set; } = null;

        #endregion
    }
}
