namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Kind of model endpoint (docs/AI_CONTRACT.md section 1.2).
    /// </summary>
    public enum ModelEndpointKindEnum
    {
        /// <summary>
        /// Chat and text generation.
        /// </summary>
        Completion,

        /// <summary>
        /// Text embeddings.
        /// </summary>
        Embedding,

        /// <summary>
        /// Document reranking.
        /// </summary>
        Rerank
    }
}
