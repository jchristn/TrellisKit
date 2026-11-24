namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Data of the message.deleted frame.
    /// </summary>
    public class MessageDeletedFrameData
    {
        #region Public-Members

        /// <summary>
        /// Conversation identifier.
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// Message identifier.
        /// </summary>
        public string MessageId { get; set; } = String.Empty;

        /// <summary>
        /// Sequence number.
        /// </summary>
        public long Seq { get; set; } = 0;

        #endregion
    }
}
