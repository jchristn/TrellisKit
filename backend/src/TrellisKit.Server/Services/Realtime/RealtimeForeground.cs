namespace TrellisKit.Server.Services.Realtime
{
    /// <summary>
    /// The push decision input of one member (docs/PHASE5_CONTRACT.md section 4.9), from
    /// <see cref="RealtimeConnectionRegistry.ForegroundAsync"/>.
    /// Thread safety: not thread safe; a snapshot.
    /// </summary>
    public sealed class RealtimeForeground
    {
        #region Public-Members

        /// <summary>
        /// At least one connection of the member, on any task, is in Foreground and was seen within 90 seconds: deliver
        /// over the socket and send no push.
        /// </summary>
        public bool HasForeground { get; set; } = false;

        /// <summary>
        /// One of those foreground connections has the asked conversation open (activeConversationId): no banner either.
        /// </summary>
        public bool ConversationActive { get; set; } = false;

        /// <summary>
        /// Connections seen within 90 seconds (any state).
        /// </summary>
        public int RecentConnections { get; set; } = 0;

        #endregion
    }
}
