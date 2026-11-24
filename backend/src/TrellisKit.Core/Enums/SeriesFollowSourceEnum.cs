namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How a series follow started.
    /// </summary>
    public enum SeriesFollowSourceEnum
    {
        /// <summary>
        /// The member followed the series.
        /// </summary>
        Explicit,

        /// <summary>
        /// Automatic follow when the member completed a lesson of the series (library.autoFollowOnProgress).
        /// </summary>
        Progress
    }
}
