namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Lifecycle status of a series or a lesson.
    /// </summary>
    public enum LibraryStatusEnum
    {
        /// <summary>
        /// Not published.
        /// </summary>
        Draft,

        /// <summary>
        /// Publishes at publishAtUtc (lessons only).
        /// </summary>
        Scheduled,

        /// <summary>
        /// Waiting for its video to become Ready (lessons only).
        /// </summary>
        Processing,

        /// <summary>
        /// Listed for callers who may read it.
        /// </summary>
        Published,

        /// <summary>
        /// Hidden from lists and search; still playable by members with progress for library.archivedPlaybackDays.
        /// </summary>
        Archived
    }
}
