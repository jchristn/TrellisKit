namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Verdict of the moderation model.
    /// </summary>
    public enum AssistantModerationVerdictEnum
    {
        /// <summary>
        /// Allowed.
        /// </summary>
        Allow,

        /// <summary>
        /// Allowed and flagged for review.
        /// </summary>
        Flag,

        /// <summary>
        /// Blocked.
        /// </summary>
        Block,

        /// <summary>
        /// The classifier failed (fail open).
        /// </summary>
        Error
    }
}
