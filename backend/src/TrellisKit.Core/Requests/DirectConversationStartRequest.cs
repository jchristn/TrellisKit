namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of POST /api/v1/conversations.
    /// </summary>
    public class DirectConversationStartRequest
    {
        #region Public-Members

        /// <summary>
        /// The member to message.
        /// </summary>
        public string? RecipientUserId { get; set; } = null;

        /// <summary>
        /// Optional first message.
        /// </summary>
        public DirectMessageSendRequest? Message { get; set; } = null;

        #endregion
    }
}
