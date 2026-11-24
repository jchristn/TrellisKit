namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Where a conversation title came from.
    /// </summary>
    public enum AssistantTitleSourceEnum
    {
        /// <summary>
        /// The start of the first message.
        /// </summary>
        Fallback,

        /// <summary>
        /// The title model.
        /// </summary>
        Generated,

        /// <summary>
        /// Renamed by the owner.
        /// </summary>
        Member
    }
}
