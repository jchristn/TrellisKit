namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How much of a venue address members see (docs/PHASE4_CONTRACT.md section 2.1).
    /// </summary>
    public enum AddressVisibilityEnum
    {
        /// <summary>
        /// Full address and exact pin.
        /// </summary>
        Exact,

        /// <summary>
        /// Locality, region, country, and a pin rounded to 2 decimal places.
        /// </summary>
        Approximate,

        /// <summary>
        /// Locality, region, and country; no pin and not on the map.
        /// </summary>
        LocalityOnly
    }
}
