namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// A moderation action.
    /// </summary>
    public enum ModerationActionTypeEnum
    {
        /// <summary>
        /// Hide pending review.
        /// </summary>
        Hide,

        /// <summary>
        /// Make hidden content visible again.
        /// </summary>
        Unhide,

        /// <summary>
        /// Remove content (served media moves to retained storage).
        /// </summary>
        Remove,

        /// <summary>
        /// Undo a removal.
        /// </summary>
        Restore,

        /// <summary>
        /// System hide after enough distinct reporters.
        /// </summary>
        AutoHide,

        /// <summary>
        /// Reports dismissed without changing the content.
        /// </summary>
        Dismiss,

        /// <summary>
        /// A thread was pinned.
        /// </summary>
        Pin,

        /// <summary>
        /// A thread was unpinned.
        /// </summary>
        Unpin,

        /// <summary>
        /// A thread was locked.
        /// </summary>
        Lock,

        /// <summary>
        /// A thread was unlocked.
        /// </summary>
        Unlock,

        /// <summary>
        /// A thread was moved to another forum.
        /// </summary>
        Move,

        /// <summary>
        /// A moderator changed a thread's title, labels, tags, type, or accepted answer.
        /// </summary>
        EditMetadata,

        /// <summary>
        /// A member was muted in one forum.
        /// </summary>
        ForumMute,

        /// <summary>
        /// A forum mute was lifted.
        /// </summary>
        ForumUnmute,

        /// <summary>
        /// The author was warned about the content (content review queue); the content is unchanged.
        /// </summary>
        Warn
    }
}
