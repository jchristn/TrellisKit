namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The queued part of an email record (recipient, tokens, headers), stored encrypted until the message reaches a final
    /// status.
    /// </summary>
    public class EmailPayload
    {
        #region Public-Members

        /// <summary>
        /// Token values.
        /// </summary>
        public Dictionary<string, string> Tokens { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Extra headers.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        #endregion
    }
}
