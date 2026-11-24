namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Broadcast preference category (docs/PHASE5_CONTRACT.md section 8.3).
    /// </summary>
    public enum BroadcastCategoryEnum
    {
        /// <summary>
        /// Respects the Announcements preferences and email suppressions.
        /// </summary>
        Optional,

        /// <summary>
        /// Ignores the Announcements preferences (never suppressions). SystemAdmin only, with a recorded reason.
        /// </summary>
        Essential
    }
}
