namespace TrellisKit.Server.Services.Messaging
{
    /// <summary>
    /// A member's live connection state for the direct message push decision (docs/PHASE5_CONTRACT.md section 4.9), as
    /// reported by the realtime connection registry.
    /// Thread safety: not thread safe; treat an instance as immutable once returned.
    /// </summary>
    public class RealtimePresence
    {
        #region Public-Members

        /// <summary>
        /// A presence without connections (the default probe result).
        /// </summary>
        public static RealtimePresence None
        {
            get { return new RealtimePresence(); }
        }

        /// <summary>
        /// Whether the member has at least one connection in the Foreground state seen within 90 seconds. When true, the
        /// socket delivers the message and no push is sent.
        /// </summary>
        public bool HasForeground { get; set; } = false;

        /// <summary>
        /// Whether a foreground connection has the conversation open (activeConversationId); the client shows no banner.
        /// </summary>
        public bool HasConversationActive { get; set; } = false;

        #endregion
    }
}
