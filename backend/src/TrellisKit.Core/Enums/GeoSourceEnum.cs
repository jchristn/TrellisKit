namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Where a venue position came from (docs/PHASE4_CONTRACT.md section 2.1).
    /// </summary>
    public enum GeoSourceEnum
    {
        /// <summary>
        /// No position (needs a pin).
        /// </summary>
        None,

        /// <summary>
        /// The configured geocoder.
        /// </summary>
        Geocoder,

        /// <summary>
        /// Staff placed the pin.
        /// </summary>
        Manual
    }
}
