namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Lifecycle status of a directory venue (docs/PHASE4_CONTRACT.md section 2.2).
    /// </summary>
    public enum VenueStatusEnum
    {
        /// <summary>
        /// Listed.
        /// </summary>
        Active,

        /// <summary>
        /// Permanently closed; kept for history and hidden from lists unless filters.includeClosed.
        /// </summary>
        Closed,

        /// <summary>
        /// Hidden from members (staff only).
        /// </summary>
        Archived
    }
}
