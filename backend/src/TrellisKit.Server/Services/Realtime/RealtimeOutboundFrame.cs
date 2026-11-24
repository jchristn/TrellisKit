namespace TrellisKit.Server.Services.Realtime
{
    using System;

    /// <summary>
    /// One serialized server frame waiting in a connection's outbound queue, or a close marker.
    /// Thread safety: immutable.
    /// </summary>
    public sealed class RealtimeOutboundFrame
    {
        #region Public-Members

        /// <summary>
        /// Frame type (metrics and overflow rules).
        /// </summary>
        public string Type { get; }

        /// <summary>
        /// Serialized frame (empty for a close marker).
        /// </summary>
        public string Json { get; }

        /// <summary>
        /// Approximate size in bytes.
        /// </summary>
        public int Bytes { get; }

        /// <summary>
        /// Conversation the frame belongs to (seq ordering), or null.
        /// </summary>
        public string? ConversationId { get; }

        /// <summary>
        /// Sequence within the conversation, or null.
        /// </summary>
        public long? Seq { get; }

        /// <summary>
        /// Dropped first on overflow (typing and receipts).
        /// </summary>
        public bool Droppable { get; }

        /// <summary>
        /// Close code when this is a close marker, else null.
        /// </summary>
        public int? CloseCode { get; }

        /// <summary>
        /// Close reason of a close marker.
        /// </summary>
        public string CloseReason { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a frame.
        /// </summary>
        /// <param name="type">Frame type.</param>
        /// <param name="json">Serialized frame.</param>
        /// <param name="conversationId">Conversation, or null.</param>
        /// <param name="seq">Sequence, or null.</param>
        /// <param name="droppable">Dropped first on overflow.</param>
        /// <exception cref="ArgumentNullException">The type or JSON is null.</exception>
        public RealtimeOutboundFrame(string type, string json, string? conversationId, long? seq, bool droppable)
        {
            Type = type ?? throw new ArgumentNullException(nameof(type));
            Json = json ?? throw new ArgumentNullException(nameof(json));
            Bytes = json.Length;
            ConversationId = conversationId;
            Seq = seq;
            Droppable = droppable;
            CloseCode = null;
            CloseReason = String.Empty;
        }

        private RealtimeOutboundFrame(int closeCode, string reason)
        {
            Type = "close";
            Json = String.Empty;
            Bytes = 0;
            CloseCode = closeCode;
            CloseReason = reason ?? String.Empty;
        }

        /// <summary>
        /// A close marker: the writer closes the socket with this code after the frames queued before it.
        /// </summary>
        /// <param name="code">Close code.</param>
        /// <param name="reason">Reason (at most 123 bytes).</param>
        /// <returns>Marker.</returns>
        public static RealtimeOutboundFrame Close(int code, string reason)
        {
            return new RealtimeOutboundFrame(code, reason);
        }

        #endregion
    }
}
