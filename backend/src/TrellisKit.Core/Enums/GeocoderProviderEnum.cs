namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Geocoding provider of a property (docs/PHASE4_CONTRACT.md section 3.5).
    /// </summary>
    public enum GeocoderProviderEnum
    {
        /// <summary>
        /// No automatic geocoding; staff place pins.
        /// </summary>
        None,

        /// <summary>
        /// A Nominatim compatible endpoint.
        /// </summary>
        Nominatim,

        /// <summary>
        /// Amazon Location Service Places.
        /// </summary>
        AwsLocation,

        /// <summary>
        /// A Pelias or Photon style GET /search?text= GeoJSON endpoint.
        /// </summary>
        Http
    }
}
