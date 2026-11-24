namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Bounded preview of a conversation's messages.
    /// </summary>
    public class MessagePageView
    {
        #region Public-Members

        /// <summary>
        /// Up to 20 newest messages, oldest first.
        /// </summary>
        public List<MessageView> Objects { get; set; } = new List<MessageView>();

        /// <summary>
        /// Messages in the conversation.
        /// </summary>
        public long TotalRecords { get; set; } = 0;

        /// <summary>
        /// Route of the full enumeration.
        /// </summary>
        public string Enumerate { get; set; } = String.Empty;

        #endregion
    }
}
