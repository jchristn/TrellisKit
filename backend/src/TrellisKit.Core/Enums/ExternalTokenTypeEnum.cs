namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Kind of token presented by an external provider.
    /// </summary>
    public enum ExternalTokenTypeEnum
    {
        /// <summary>
        /// OpenID Connect identity token (JWT).
        /// </summary>
        Jwt,

        /// <summary>
        /// OAuth access token (Facebook classic login).
        /// </summary>
        AccessToken
    }
}
