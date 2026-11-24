namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A composed email before layout: subject, preheader, heading, and body blocks with every token already filled.
    /// An <see cref="EmailLayout"/> turns it into the final HTML and text parts.
    /// </summary>
    public class EmailDocument
    {
        #region Public-Members

        /// <summary>
        /// Subject (one line).
        /// </summary>
        public string Subject { get; set; } = String.Empty;

        /// <summary>
        /// Preheader (inbox preview text, one line).
        /// </summary>
        public string Preheader { get; set; } = String.Empty;

        /// <summary>
        /// Heading at the top of the message.
        /// </summary>
        public string Heading { get; set; } = String.Empty;

        /// <summary>
        /// Body blocks in order.
        /// </summary>
        public List<EmailBlock> Blocks { get; set; } = new List<EmailBlock>();

        #endregion
    }
}
