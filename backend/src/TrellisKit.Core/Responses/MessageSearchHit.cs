namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// One hit of POST /api/v1/messages/search/enumerate (as-built note B).
    /// </summary>
    public class MessageSearchHit
    {
        #region Public-Members

        /// <summary>
        /// The message.
        /// </summary>
        public DirectMessageView Message { get; set; } = new DirectMessageView();

        /// <summary>
        /// Conversation identifier.
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// The other member of the conversation.
        /// </summary>
        public UserSummary? OtherMember { get; set; } = null;

        /// <summary>
        /// Text around the matches, with matches wrapped in double asterisks.
        /// </summary>
        public string Headline { get; set; } = String.Empty;

        #endregion
    }
}
