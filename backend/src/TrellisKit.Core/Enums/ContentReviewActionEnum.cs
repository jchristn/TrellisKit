namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// What an administrator does with a content changelog row. Hide and Remove run the moderation action on the
    /// content; Warn records a moderation action and notifies the author; Mute and Ban run the member actions.
    /// </summary>
    public enum ContentReviewActionEnum
    {
        /// <summary>
        /// Nothing to act on.
        /// </summary>
        Dismiss,

        /// <summary>
        /// Hide the content (moderation action Hide).
        /// </summary>
        Hide,

        /// <summary>
        /// Remove the content (moderation action Remove).
        /// </summary>
        Remove,

        /// <summary>
        /// Warn the author (moderation action Warn and a notification).
        /// </summary>
        Warn,

        /// <summary>
        /// Mute the author.
        /// </summary>
        Mute,

        /// <summary>
        /// Ban the author.
        /// </summary>
        Ban
    }
}
