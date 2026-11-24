namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How a comment is shown to the caller.
    /// </summary>
    public enum CommentStateEnum
    {
        /// <summary>
        /// Full content.
        /// </summary>
        Visible,

        /// <summary>
        /// Deleted by its author, an administrator, or account deletion; shown as a placeholder.
        /// </summary>
        Deleted,

        /// <summary>
        /// Removed by a moderator; shown as a placeholder except to moderators and the author.
        /// </summary>
        Removed,

        /// <summary>
        /// Hidden pending review; shown as a placeholder except to moderators and the author.
        /// </summary>
        Hidden,

        /// <summary>
        /// Written by a member the caller blocked; shown as a placeholder.
        /// </summary>
        BlockedAuthor
    }
}
