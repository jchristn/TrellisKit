namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Precision of the position a caller receives (docs/PHASE4_CONTRACT.md section 2.1).
    /// </summary>
    public enum GeoPrecisionEnum
    {
        /// <summary>
        /// 5 decimal places.
        /// </summary>
        Exact,

        /// <summary>
        /// 2 decimal places.
        /// </summary>
        Approximate,

        /// <summary>
        /// No position.
        /// </summary>
        None
    }
}
