namespace TrellisKit.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// How a CLI agent harness authenticates (docs/HARNESSES.md, "Harness profiles").
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HarnessCredentialTypeEnum
    {
        /// <summary>
        /// The vendor credential variables of the worker's own environment (the default).
        /// </summary>
        Environment,

        /// <summary>
        /// A stored vendor API key.
        /// </summary>
        ApiKey,

        /// <summary>
        /// A stored bearer token for a gateway (sent with the profile's base URL).
        /// </summary>
        AuthToken,

        /// <summary>
        /// A stored long-lived OAuth token.
        /// </summary>
        OAuthToken
    }
}
