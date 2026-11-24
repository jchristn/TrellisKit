namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A row of realtimeconnections (docs/PHASE5_CONTRACT.md section 4.11): one live WebSocket connection of a member on
    /// one server task. Upserted on hello and state, touched every 60 seconds, deleted on disconnect, and pruned when not
    /// seen for 5 minutes. Used internally for the push decision only (no presence is shown to members).
    /// </summary>
    public class RealtimeConnectionRecord
    {
        /// <summary>
        /// Identifier (rtc_), also the welcome frame's connectionId.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Property the connection belongs to (filled from the binding on reads).
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Session of the access token presented in hello.
        /// </summary>
        public string SessionId { get; set; } = String.Empty;

        /// <summary>
        /// Server task holding the socket (tsk_...).
        /// </summary>
        public string TaskId { get; set; } = String.Empty;

        /// <summary>
        /// Client kind from hello (Web, Mobile, Other).
        /// </summary>
        public string Client { get; set; } = "Other";

        /// <summary>
        /// App state: Foreground or Background.
        /// </summary>
        public string AppState { get; set; } = "Foreground";

        /// <summary>
        /// Conversation open on screen, or null.
        /// </summary>
        public string? ActiveConversationId { get; set; } = null;

        /// <summary>
        /// UTC time of the hello.
        /// </summary>
        public DateTime ConnectedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC time the row was last upserted or touched.
        /// </summary>
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    }
}
