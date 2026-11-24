namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One message of a test exchange.
    /// </summary>
    public class AssistantTestMessage
    {
        #region Public-Members

        /// <summary>
        /// user or assistant.
        /// </summary>
        public string? Role { get; set; } = null;

        /// <summary>
        /// Content.
        /// </summary>
        public string? Content { get; set; } = null;

        #endregion
    }
}
