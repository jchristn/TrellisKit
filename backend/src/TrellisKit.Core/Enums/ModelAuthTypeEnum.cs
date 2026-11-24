namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How a model endpoint authenticates (docs/AI_CONTRACT.md section 3.2).
    /// </summary>
    public enum ModelAuthTypeEnum
    {
        /// <summary>
        /// No credentials (extra headers only).
        /// </summary>
        None,

        /// <summary>
        /// Authorization: Bearer with the stored value.
        /// </summary>
        Bearer,

        /// <summary>
        /// The provider's native API key scheme.
        /// </summary>
        ApiKey,

        /// <summary>
        /// A custom header name with the stored value.
        /// </summary>
        Header,

        /// <summary>
        /// A query parameter appended to every request.
        /// </summary>
        QueryParameter,

        /// <summary>
        /// HTTP Basic with the username and the stored value as the password.
        /// </summary>
        Basic
    }
}
