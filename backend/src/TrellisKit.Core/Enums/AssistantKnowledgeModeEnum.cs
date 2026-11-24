namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How an assistant retrieves knowledge.
    /// </summary>
    public enum AssistantKnowledgeModeEnum
    {
        /// <summary>
        /// Keyword search through the content enumerations.
        /// </summary>
        Keyword,

        /// <summary>
        /// Vector search in the vector store.
        /// </summary>
        Vector,

        /// <summary>
        /// Keyword and vector results merged by reciprocal rank fusion.
        /// </summary>
        Hybrid
    }
}
