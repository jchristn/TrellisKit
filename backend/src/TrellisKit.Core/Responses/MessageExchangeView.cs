namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Non-streaming send result: the stored member message and the finished reply.
    /// </summary>
    public class MessageExchangeView
    {
        #region Public-Members

        /// <summary>
        /// Member message.
        /// </summary>
        public MessageView UserMessage { get; set; } = new MessageView();

        /// <summary>
        /// Reply.
        /// </summary>
        public MessageView Reply { get; set; } = new MessageView();

        #endregion
    }
}
