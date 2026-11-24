namespace TrellisKit.Server.Services.Realtime
{
    using System;

    /// <summary>
    /// One server to client WebSocket frame rendered for a recipient (docs/PHASE5_CONTRACT.md sections 4.3 and 4.5): the
    /// frame type and its data object. The gateway adds the envelope (id, ts) and serializes the data with the API
    /// JSON options.
    /// Thread safety: not thread safe; treat an instance as immutable once rendered.
    /// </summary>
    public class RealtimeFrame
    {
        #region Public-Members

        /// <summary>
        /// Frame type, for example message.created.
        /// </summary>
        public string Type { get; set; } = String.Empty;

        /// <summary>
        /// Frame data (serialized as the frame's data object).
        /// </summary>
        public object Data { get; set; } = new object();

        #endregion
    }
}
