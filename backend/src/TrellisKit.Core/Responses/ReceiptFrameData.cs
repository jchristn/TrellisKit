namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Data of the receipt.read and receipt.delivered frames.
    /// </summary>
    public class ReceiptFrameData
    {
        #region Public-Members

        /// <summary>
        /// Conversation identifier.
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// Member whose position moved.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Position.
        /// </summary>
        public long Seq { get; set; } = 0;

        #endregion
    }
}
