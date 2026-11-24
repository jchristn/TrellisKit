namespace TrellisKit.Server.Services.Realtime
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One realtime bus event (docs/PHASE5_CONTRACT.md section 4.11). It carries references only, never message text
    /// or personal data, and serializes to at most 1 KB with single-letter keys. Receiving tasks load the referenced
    /// rows through the property-bound driver of <see cref="PropertyId"/> before rendering frames.
    /// Thread safety: not thread safe; treat an instance as immutable once published.
    /// </summary>
    public class RealtimeEvent
    {
        #region Public-Members

        /// <summary>
        /// Payload version. Default 1.
        /// </summary>
        [JsonPropertyName("v")]
        public int Version { get; set; } = 1;

        /// <summary>
        /// Property id the event belongs to. Receivers drop events of properties they do not serve on the socket.
        /// </summary>
        [JsonPropertyName("p")]
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Event kind, one of <see cref="RealtimeEventKinds"/>.
        /// </summary>
        [JsonPropertyName("k")]
        public string Kind { get; set; } = String.Empty;

        /// <summary>
        /// Conversation id (message, receipt, typing, and conversation kinds), or null.
        /// </summary>
        [JsonPropertyName("c")]
        public string? ConversationId { get; set; } = null;

        /// <summary>
        /// Message sequence number or receipt position, or null.
        /// </summary>
        [JsonPropertyName("s")]
        public long? Seq { get; set; } = null;

        /// <summary>
        /// Message id, or null.
        /// </summary>
        [JsonPropertyName("m")]
        public string? MessageId { get; set; } = null;

        /// <summary>
        /// Recipient user ids (bounded by the conversation size or one user for fan-out kinds). Never null.
        /// </summary>
        [JsonPropertyName("u")]
        public List<string> UserIds
        {
            get { return _UserIds; }
            set { _UserIds = value ?? new List<string>(); }
        }

        /// <summary>
        /// Acting user id (the sender of a message, the member typing or reading), or null.
        /// </summary>
        [JsonPropertyName("a")]
        public string? ActorUserId { get; set; } = null;

        /// <summary>
        /// Session id (session.revoked), or null.
        /// </summary>
        [JsonPropertyName("x")]
        public string? SessionId { get; set; } = null;

        /// <summary>
        /// Other target id (a ticket id for ticket.updated, a notification id for notification.created), or null.
        /// </summary>
        [JsonPropertyName("t")]
        public string? TargetId { get; set; } = null;

        /// <summary>
        /// Short kind-specific state (typing Start or Stop, a ticket status, a feature key), or null.
        /// </summary>
        [JsonPropertyName("d")]
        public string? State { get; set; } = null;

        /// <summary>
        /// Unique event id (for logging and de-duplication).
        /// </summary>
        [JsonPropertyName("e")]
        public string EventId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// UTC time the event was published (bus lag measurement).
        /// </summary>
        [JsonPropertyName("w")]
        public DateTime PublishedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private List<string> _UserIds = new List<string>();

        #endregion
    }
}
