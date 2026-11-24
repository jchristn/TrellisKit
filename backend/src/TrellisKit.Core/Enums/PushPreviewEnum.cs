namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// What a direct message push shows (docs/PHASE5_CONTRACT.md section 1.2).
    /// </summary>
    public enum PushPreviewEnum
    {
        /// <summary>
        /// The sender name only.
        /// </summary>
        SenderOnly,

        /// <summary>
        /// The sender name and a short excerpt.
        /// </summary>
        Full,

        /// <summary>
        /// A generic text without the sender.
        /// </summary>
        Hidden
    }
}
