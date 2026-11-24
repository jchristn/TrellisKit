namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Data of the typing frame.
    /// </summary>
    public class TypingFrameData
    {
        #region Public-Members

        /// <summary>
        /// Conversation identifier.
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// Typing member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Start or Stop.
        /// </summary>
        public string State { get; set; } = "Start";

        /// <summary>
        /// Clients clear the indicator after this many milliseconds without a refresh.
        /// </summary>
        public int ExpiresInMs { get; set; } = 6000;

        #endregion
    }
}
