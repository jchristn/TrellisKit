namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Moderation model behavior of an assistant.
    /// </summary>
    public enum AssistantModerationModeEnum
    {
        /// <summary>
        /// No classification.
        /// </summary>
        Off,

        /// <summary>
        /// Record verdicts only.
        /// </summary>
        LogOnly,

        /// <summary>
        /// Block flagged input and output.
        /// </summary>
        Block
    }
}
