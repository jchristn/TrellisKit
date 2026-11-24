namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How an app content version was created.
    /// </summary>
    public enum ContentVersionKindEnum
    {
        /// <summary>
        /// Published from pending drafts.
        /// </summary>
        Publish,

        /// <summary>
        /// Restored the content of an earlier version.
        /// </summary>
        Restore
    }
}
