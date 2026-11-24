namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Ingestion status of one knowledge item.
    /// </summary>
    public enum AiKnowledgeItemStatusEnum
    {
        /// <summary>
        /// Waiting to be indexed.
        /// </summary>
        Pending,

        /// <summary>
        /// Embedded and stored.
        /// </summary>
        Indexed,

        /// <summary>
        /// Indexing failed.
        /// </summary>
        Failed,

        /// <summary>
        /// No longer selected; vectors deleted.
        /// </summary>
        Removed
    }
}
