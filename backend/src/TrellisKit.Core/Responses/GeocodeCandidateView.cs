namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Models;

    /// <summary>
    /// A place search candidate (docs/PHASE4_CONTRACT.md section 3.1).
    /// </summary>
    public class GeocodeCandidateView : CatalogEntryMetadata
    {
        #region Public-Members

        /// <summary>Stable hash of the provider candidate's label and rounded coordinates.</summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>Zero-based provider relevance rank within its finite candidate result.</summary>
        public int Rank { get; set; }


        /// <summary>
        /// Display label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Latitude.
        /// </summary>
        public double Lat { get; set; } = 0;

        /// <summary>
        /// Longitude.
        /// </summary>
        public double Lng { get; set; } = 0;

        /// <summary>
        /// Extent, or null.
        /// </summary>
        public GeoBox? Bbox { get; set; } = null;

        #endregion
    }
}
