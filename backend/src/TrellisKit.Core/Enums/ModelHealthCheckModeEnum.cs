namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How a health check probes an endpoint.
    /// </summary>
    public enum ModelHealthCheckModeEnum
    {
        /// <summary>
        /// PolyPrompt ValidateConnectivityAsync.
        /// </summary>
        Connectivity,

        /// <summary>
        /// A one-token prompt.
        /// </summary>
        Completion,

        /// <summary>
        /// Embed the word ping.
        /// </summary>
        Embedding,

        /// <summary>
        /// Rerank two documents.
        /// </summary>
        Rerank
    }
}
