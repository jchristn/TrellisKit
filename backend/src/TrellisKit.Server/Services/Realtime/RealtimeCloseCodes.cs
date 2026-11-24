namespace TrellisKit.Server.Services.Realtime
{
    /// <summary>
    /// WebSocket close codes of the realtime protocol (docs/PHASE5_CONTRACT.md section 4.10).
    /// Thread safety: constants only.
    /// </summary>
    public static class RealtimeCloseCodes
    {
        #region Public-Members

        /// <summary>
        /// Normal closure (also used when a silent connection is dropped).
        /// </summary>
        public const int Normal = 1000;

        /// <summary>
        /// Binary frame received.
        /// </summary>
        public const int UnsupportedData = 1003;

        /// <summary>
        /// Frame larger than realtime.maxInboundFrameBytes (sent by Watson).
        /// </summary>
        public const int TooLarge = 1009;

        /// <summary>
        /// Server error.
        /// </summary>
        public const int ServerError = 1011;

        /// <summary>
        /// Service restart (draining, after shutdown).
        /// </summary>
        public const int Restart = 1012;

        /// <summary>
        /// Try again later (slow consumer).
        /// </summary>
        public const int TryAgainLater = 1013;

        /// <summary>
        /// Protocol violation (frame before hello, malformed JSON, unknown protocol).
        /// </summary>
        public const int ProtocolViolation = 4400;

        /// <summary>
        /// Unauthorized (bad, expired, revoked token, disabled property).
        /// </summary>
        public const int Unauthorized = 4401;

        /// <summary>
        /// Membership required for every requested capability.
        /// </summary>
        public const int MembershipRequired = 4402;

        /// <summary>
        /// Property not found or mismatch.
        /// </summary>
        public const int PropertyMismatch = 4404;

        /// <summary>
        /// hello timeout.
        /// </summary>
        public const int HelloTimeout = 4408;

        /// <summary>
        /// Replaced: the member exceeded realtime.maxConnectionsPerUser.
        /// </summary>
        public const int Replaced = 4409;

        /// <summary>
        /// Rate limited.
        /// </summary>
        public const int RateLimited = 4429;

        /// <summary>
        /// realtime kill switch on.
        /// </summary>
        public const int FeatureDisabled = 4503;

        #endregion
    }
}
