namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Partio chunking strategy (Partio REST ChunkingConfiguration.Strategy).
    /// </summary>
    public enum PartioChunkingStrategyEnum
    {
        /// <summary>
        /// Fixed number of tokens per chunk.
        /// </summary>
        FixedTokenCount,

        /// <summary>
        /// Chunks on sentence boundaries.
        /// </summary>
        SentenceBased,

        /// <summary>
        /// Chunks on paragraph boundaries.
        /// </summary>
        ParagraphBased
    }
}
