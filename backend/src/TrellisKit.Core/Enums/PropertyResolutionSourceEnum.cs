namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How a request was bound to its property.
    /// </summary>
    public enum PropertyResolutionSourceEnum
    {
        /// <summary>
        /// The X-TK-Property header.
        /// </summary>
        Header,

        /// <summary>
        /// The request host matched a property domain.
        /// </summary>
        Host,

        /// <summary>
        /// A platform host (platform routes only).
        /// </summary>
        Platform,

        /// <summary>
        /// Single-property mode: the only property.
        /// </summary>
        Single,

        /// <summary>
        /// Resolved from verified webhook content or its URL.
        /// </summary>
        Webhook,

        /// <summary>
        /// Deployment-level route that needs no property (health, OpenAPI, CORS preflight).
        /// </summary>
        Special
    }
}
