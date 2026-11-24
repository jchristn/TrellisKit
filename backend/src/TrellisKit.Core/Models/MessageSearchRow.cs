namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// One full text search hit over a member's messages.
    /// </summary>
    public class MessageSearchRow
    {
        #region Public-Members

        /// <summary>
        /// Message.
        /// </summary>
        public Message Message { get; set; } = new Message();

        /// <summary>
        /// Text with matches wrapped in double asterisks.
        /// </summary>
        public string Headline { get; set; } = String.Empty;

        #endregion
    }
}
