namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Authorization role. User is implicit for every account and never stored.
    /// </summary>
    public enum RoleEnum
    {
        /// <summary>
        /// Implicit role of every account.
        /// </summary>
        User,

        /// <summary>
        /// Full administrative access.
        /// </summary>
        SystemAdmin,

        /// <summary>
        /// Forum moderator (scoped by forum id or *).
        /// </summary>
        Moderator,

        /// <summary>
        /// Main feed moderator.
        /// </summary>
        FeedModerator,

        /// <summary>
        /// Venue and instructor directory moderator.
        /// </summary>
        DirectoryModerator,

        /// <summary>
        /// Creates and manages events.
        /// </summary>
        EventManager,

        /// <summary>
        /// Manages library series, lessons, and their media without full administrative rights.
        /// </summary>
        LibraryEditor,

        /// <summary>
        /// Works the support ticket inbox and the ideas board (global).
        /// </summary>
        SupportAgent,

        /// <summary>
        /// Creates and sends broadcasts (global).
        /// </summary>
        Communicator,

        /// <summary>
        /// Manages live update releases, channels, rollouts, feature flags, kill switches, and app content without full
        /// administrative rights (global; signing and deploy keys stay SystemAdmin only).
        /// </summary>
        ReleaseManager
    }
}
