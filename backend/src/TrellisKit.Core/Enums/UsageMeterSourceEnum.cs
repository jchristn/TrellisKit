namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Where a usage meter comes from.
    /// </summary>
    public enum UsageMeterSourceEnum
    {
        /// <summary>
        /// Recomputed from the engine tables.
        /// </summary>
        Derived,

        /// <summary>
        /// Counted on write by each server instance (no source table).
        /// </summary>
        Recorded,

        /// <summary>
        /// Sizes read from the object store.
        /// </summary>
        Measured
    }
}
