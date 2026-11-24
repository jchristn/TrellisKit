namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Moderation state stored on content.
    /// </summary>
    public enum ModerationStateEnum
    {
        /// <summary>
        /// Not moderated.
        /// </summary>
        None,

        /// <summary>
        /// Hidden pending review.
        /// </summary>
        Hidden,

        /// <summary>
        /// Removed by a moderator.
        /// </summary>
        Removed
    }
}
