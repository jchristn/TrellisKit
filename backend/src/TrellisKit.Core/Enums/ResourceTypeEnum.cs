namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Resource type a role assignment is scoped to.
    /// </summary>
    public enum ResourceTypeEnum
    {
        /// <summary>
        /// The whole platform.
        /// </summary>
        Global,

        /// <summary>
        /// A forum (resource id is a forum id or *).
        /// </summary>
        Forum,

        /// <summary>
        /// The main feed.
        /// </summary>
        Feed,

        /// <summary>
        /// The venue and instructor directory.
        /// </summary>
        Directory,

        /// <summary>
        /// Events and seminars.
        /// </summary>
        Events,

        /// <summary>
        /// The library (series and lessons).
        /// </summary>
        Library,

        /// <summary>
        /// Direct message report queue (Moderator scope).
        /// </summary>
        Messaging
    }
}
