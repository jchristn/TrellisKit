namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Kind of client that created a session (from the X-Client header).
    /// </summary>
    public enum ClientTypeEnum
    {
        /// <summary>
        /// The website (refresh token in an HttpOnly cookie).
        /// </summary>
        Web,

        /// <summary>
        /// The mobile app (refresh token in the response body).
        /// </summary>
        Mobile,

        /// <summary>
        /// Any other client, for example curl or automation (refresh token in the response body).
        /// </summary>
        Other
    }
}
