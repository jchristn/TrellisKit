namespace TrellisKit.Server.Services.Messaging
{
    using System;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Result of POST /api/v1/conversations: the payload and whether the conversation is new (201) or existing (200).
    /// </summary>
    public class ConversationStartOutcome
    {
        #region Public-Members

        /// <summary>
        /// Payload.
        /// </summary>
        public ConversationStartResponse Response { get; set; } = new ConversationStartResponse();

        /// <summary>
        /// True when the conversation was created.
        /// </summary>
        public bool Created { get; set; } = false;

        #endregion
    }
}
