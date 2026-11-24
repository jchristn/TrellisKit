namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Kind of a map feature (docs/PHASE4_CONTRACT.md section 3.3).
    /// </summary>
    public enum MapFeatureKindEnum
    {
        /// <summary>
        /// One venue or event.
        /// </summary>
        Point,

        /// <summary>
        /// Several venues or events in one grid cell.
        /// </summary>
        Cluster
    }
}
