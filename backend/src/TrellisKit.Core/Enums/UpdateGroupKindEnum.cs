namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How a live update group was created.
    /// </summary>
    public enum UpdateGroupKindEnum
    {
        /// <summary>
        /// Uploaded by the publish tool, or promoted from another channel.
        /// </summary>
        Publish,

        /// <summary>
        /// A rollback that republishes an earlier group's content with new update ids.
        /// </summary>
        Republish,

        /// <summary>
        /// Answers installs with the rollBackToEmbedded directive.
        /// </summary>
        RollBackToEmbedded
    }
}
