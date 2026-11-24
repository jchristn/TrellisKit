namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Provider API spoken by a model endpoint (docs/AI_CONTRACT.md section 3.1).
    /// </summary>
    public enum ModelApiTypeEnum
    {
        /// <summary>
        /// Ollama.
        /// </summary>
        Ollama,

        /// <summary>
        /// OpenAI.
        /// </summary>
        OpenAI,

        /// <summary>
        /// An OpenAI-compatible server.
        /// </summary>
        OpenAICompatible,

        /// <summary>
        /// Google Gemini.
        /// </summary>
        Gemini,

        /// <summary>
        /// Azure OpenAI (endpointUrl is the resource endpoint; providerSettings.deployment required, apiVersion optional; ApiKey or Bearer auth).
        /// </summary>
        AzureOpenAI,

        /// <summary>
        /// Anthropic.
        /// </summary>
        Anthropic,

        /// <summary>
        /// AWS Bedrock (providerSettings.region; ApiKey with providerSettings.accessKeyId, or None for environment credentials; SigV4 signed).
        /// </summary>
        Bedrock,

        /// <summary>
        /// Google Vertex AI (providerSettings.project and region; ApiKey with the service account JSON, or None for application default credentials).
        /// </summary>
        VertexAI,

        /// <summary>
        /// Ollama Cloud.
        /// </summary>
        OllamaCloud,

        /// <summary>
        /// xAI.
        /// </summary>
        XAI,

        /// <summary>
        /// Cohere.
        /// </summary>
        Cohere,

        /// <summary>
        /// Voyage AI.
        /// </summary>
        VoyageAI,

        /// <summary>
        /// Hugging Face Text Embeddings Inference.
        /// </summary>
        Tei
    }
}
