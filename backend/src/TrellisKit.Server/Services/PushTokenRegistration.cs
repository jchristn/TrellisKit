namespace TrellisKit.Server.Services
{
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Result of registering a push token.
    /// </summary>
    public class PushTokenRegistration
    {
        /// <summary>
        /// The token as returned by the API.
        /// </summary>
        public PushTokenView Token { get; set; } = new PushTokenView();

        /// <summary>
        /// True when the token was new (201), false when it already existed (200).
        /// </summary>
        public bool Created { get; set; } = false;
    }
}
