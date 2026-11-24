namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Result of re-encrypting stored secrets with the current key.
    /// </summary>
    public class ReencryptResponse
    {
        #region Public-Members

        /// <summary>
        /// Endpoints rewritten.
        /// </summary>
        public int Endpoints { get; set; } = 0;

        /// <summary>
        /// Secrets rewritten.
        /// </summary>
        public int Secrets { get; set; } = 0;

        /// <summary>
        /// Current key id.
        /// </summary>
        public string KeyId { get; set; } = String.Empty;

        #endregion
    }
}
