namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Time window filter of event lists (docs/PHASE4_CONTRACT.md section 4.2).
    /// </summary>
    public enum EventWhenEnum
    {
        /// <summary>
        /// Events that have not ended.
        /// </summary>
        Upcoming,

        /// <summary>
        /// Events that have ended.
        /// </summary>
        Past,

        /// <summary>
        /// Every event.
        /// </summary>
        All
    }
}
