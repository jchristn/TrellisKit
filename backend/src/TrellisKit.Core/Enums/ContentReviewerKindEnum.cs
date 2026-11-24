namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Who reviewed a content changelog row.
    /// </summary>
    public enum ContentReviewerKindEnum
    {
        /// <summary>
        /// A model run through the Moderation task role.
        /// </summary>
        Model,

        /// <summary>
        /// An administrator in the content review queue.
        /// </summary>
        Administrator,

        /// <summary>
        /// The engine itself (deletions and rows without text need no model).
        /// </summary>
        System
    }
}
