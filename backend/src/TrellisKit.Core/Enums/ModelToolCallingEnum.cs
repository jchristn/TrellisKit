namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How an endpoint receives assistant tools (docs/AI_CONTRACT.md section 8.3).
    /// </summary>
    public enum ModelToolCallingEnum
    {
        /// <summary>
        /// Provider function calling through PolyPrompt (ChatRequest.Tools).
        /// </summary>
        Native,

        /// <summary>
        /// A JSON tool request protocol described in a system message, for models without native tool support.
        /// </summary>
        Prompt,

        /// <summary>
        /// The endpoint never receives tools.
        /// </summary>
        Off
    }
}
